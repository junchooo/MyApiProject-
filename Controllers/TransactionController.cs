using Microsoft.AspNetCore.Mvc;
using TransactionAPI.Models; 
using System;
using System.Text;
using System.Security.Cryptography;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json; 
using log4net; 
using Microsoft.AspNetCore.DataProtection;

namespace TransactionAPI.Controllers 
{
    [ApiController]
    [Route("api")]
    public class TransactionController : ControllerBase
    {
        private record PartnerCredentials(string PartnerNo, string Password);

        private readonly Dictionary<string, PartnerCredentials> _allowedPartners =
            new Dictionary<string, PartnerCredentials>(StringComparer.OrdinalIgnoreCase)
        {
            { "FAKEGOOGLE", new PartnerCredentials("FG-00001", "FAKEPASSWORD1234") },
            { "FAKEPEOPLE", new PartnerCredentials("FG-00002", "FAKEPASSWORD4578") }
        };

        private static readonly TimeSpan _allowedTimestampSkew = TimeSpan.FromMinutes(5);
        private static readonly ILog _log = LogManager.GetLogger(typeof(TransactionController));
        private readonly IDataProtector _dataProtector; // For encrypting passwords in logs

        // Inject IDataProtectionProvider to create a protector
        public TransactionController(IDataProtectionProvider protectionProvider)
        {
            _log.Debug("TransactionController constructor was CALLED.");
            // Create a protector with a specific purpose string.
            // This helps isolate protected data if you use data protection for other things.
            _dataProtector = protectionProvider.CreateProtector("TransactionAPI.Log.Password.v1");
        }

        private string EncryptForLog(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return plainText; 
            }
            try
            {
                return _dataProtector.Protect(plainText);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to encrypt data for logging.", ex);
                return "[ENCRYPTION_FAILED]"; 
            }
        }

        // Helper method to create a loggable version of the request, encrypting sensitive data
        private object GetLoggableRequest(SubmitTransactionRequest request)
        {
            return new
            {
                request.PartnerKey,
                request.PartnerRefNo,
                PartnerPassword = EncryptForLog(request.PartnerPassword), 
                request.TotalAmount,
                request.Items, 
                request.Timestamp
            };
        }

        // Helper method to serialize objects to JSON for logging
        private string SerializeForLog(object obj)
        {
            try
            {
                return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
            }
            catch (Exception ex)
            {
                _log.Error("Failed to serialize object for logging.", ex);
                return "Error serializing object for log.";
            }
        }

        // Main private helper method for all validations
        private (bool IsValid, IActionResult? ErrorResult, DateTime ParsedRequestTimestampUtc) PerformAllValidations(SubmitTransactionRequest request)
        {
            DateTime parsedRequestTimestampUtc = DateTime.MinValue;
            DateTime serverTime;
            string decodedPartnerPassword;
            long calculatedTotalFromItems;

            // 1. Timestamp Format and Expiration Validation
            if (string.IsNullOrEmpty(request.Timestamp) ||
                !DateTime.TryParse(request.Timestamp, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsedRequestTimestampUtc))
            {
                _log.Warn($"Validation Failed: Timestamp is invalid or missing. Provided: '{request.Timestamp}'");
                return (false, BadRequest(new TransactionResponse { Result = 0, ResultMessage = "Timestamp is invalid or missing." }), parsedRequestTimestampUtc);
            }

            serverTime = DateTime.UtcNow;
            if (parsedRequestTimestampUtc < serverTime - _allowedTimestampSkew || parsedRequestTimestampUtc > serverTime + _allowedTimestampSkew)
            {
                _log.Warn($"Validation Failed: Timestamp Expired. Request UTC: {parsedRequestTimestampUtc}, Server UTC: {serverTime}, Allowed Skew: {_allowedTimestampSkew}.");
                return (false, BadRequest(new TransactionResponse { Result = 0, ResultMessage = "Expired." }), parsedRequestTimestampUtc);
            }

            // 2. Partner Authentication
            if (string.IsNullOrEmpty(request.PartnerKey) || string.IsNullOrEmpty(request.PartnerPassword))
            {
                _log.Warn("Validation Failed: PartnerKey or PartnerPassword is null or empty.");
                return (false, Unauthorized(new TransactionResponse { Result = 0, ResultMessage = "Access Denied!" }), parsedRequestTimestampUtc);
            }

            try
            {
                byte[] passwordBytes = Convert.FromBase64String(request.PartnerPassword);
                decodedPartnerPassword = Encoding.UTF8.GetString(passwordBytes);
            }
            catch (FormatException ex)
            {
                // Log the original Base64 string that failed to decode, but not the decoded one (which wouldn't exist)
                _log.Error($"Validation Failed: PartnerPassword (original Base64: '{request.PartnerPassword}') is not a valid Base64 string.", ex);
                return (false, BadRequest(new TransactionResponse { Result = 0, ResultMessage = "PartnerPassword is not a valid Base64 string." }), parsedRequestTimestampUtc);
            }

            if (!_allowedPartners.TryGetValue(request.PartnerKey, out var partnerCreds) ||
                partnerCreds.Password != decodedPartnerPassword)
            {
                _log.Warn($"Validation Failed: Authentication failed for PartnerKey: '{request.PartnerKey}'.");
                return (false, Unauthorized(new TransactionResponse { Result = 0, ResultMessage = "Access Denied!" }), parsedRequestTimestampUtc);
            }

            // 3. Signature Validation
            if (!IsValidSignature(request, parsedRequestTimestampUtc))
            {
                 _log.Warn($"Validation Failed: Signature validation failed for PartnerKey: '{request.PartnerKey}', PartnerRefNo: '{request.PartnerRefNo}'. Received Sig: '{request.Sig}'");
                return (false, BadRequest(new TransactionResponse { Result = 0, ResultMessage = "Access Denied!" }), parsedRequestTimestampUtc);
            }

            // 4. "Invalid Total Amount" Validation (if items are provided)
            if (request.Items != null && request.Items.Any())
            {
                calculatedTotalFromItems = 0;
                foreach (var item in request.Items)
                {
                    if (item.Qty <= 0)
                    {
                        _log.Warn($"Validation Failed: Item {item.PartnerItemRef} has invalid quantity ({item.Qty}).");
                        return (false, BadRequest(new TransactionResponse { Result = 0, ResultMessage = $"Item {item.PartnerItemRef} has invalid quantity (must be positive)." }), parsedRequestTimestampUtc);
                    }
                    if (item.UnitPrice <= 0)
                    {
                        _log.Warn($"Validation Failed: Item {item.PartnerItemRef} has invalid unit price ({item.UnitPrice}).");
                        return (false, BadRequest(new TransactionResponse { Result = 0, ResultMessage = $"Item {item.PartnerItemRef} has invalid unit price (must be positive)." }), parsedRequestTimestampUtc);
                    }
                    calculatedTotalFromItems += (long)item.Qty * item.UnitPrice;
                }

                if (request.TotalAmount != calculatedTotalFromItems)
                {
                    _log.Warn($"Validation Failed: Invalid Total Amount. Request TotalAmount: {request.TotalAmount}, Calculated from items: {calculatedTotalFromItems}.");
                    return (false, BadRequest(new TransactionResponse { Result = 0, ResultMessage = "Invalid Total Amount." }), parsedRequestTimestampUtc);
                }
            }
            return (true, null, parsedRequestTimestampUtc);
        }


        [HttpPost("submittrxmessage")]
        public IActionResult SubmitTransaction([FromBody] SubmitTransactionRequest request)
        {
            // Log basic info first, then the sanitized (encrypted password) body
            _log.Info($"Received request for PartnerKey: '{request.PartnerKey}', PartnerRefNo: '{request.PartnerRefNo}'.");
            _log.Info($"Sanitized Request Body (with encrypted password): {SerializeForLog(GetLoggableRequest(request))}");

            // Basic Model Validation (DataAnnotations)
            if (!ModelState.IsValid)
            {
                var errorMessages = ModelState.Values.SelectMany(v => v.Errors)
                                        .Select(e => !string.IsNullOrEmpty(e.ErrorMessage) ? e.ErrorMessage : "Invalid input.")
                                        .ToList();
                string combinedErrorMessage = string.Join("; ", errorMessages);
                if (string.IsNullOrEmpty(combinedErrorMessage)) combinedErrorMessage = "Model validation failed with one or more errors.";
                
                var errorResponse = new TransactionResponse { Result = 0, ResultMessage = combinedErrorMessage };
                _log.Warn($"Model validation failed. Response: {SerializeForLog(errorResponse)}");
                return BadRequest(errorResponse);
            }

            // Perform all other validations using the helper method
            var (isValid, errorResult, _) = PerformAllValidations(request); 
            if (!isValid && errorResult != null)
            {
                // The specific error should have been logged within PerformAllValidations before it returned.
                // Now log the final response object being returned.
                if (errorResult is ObjectResult objResult && objResult.Value is TransactionResponse tr)
                {
                     _log.Warn($"Validation pipeline failed. Returning error response: {SerializeForLog(tr)}");
                }
                else // Should not happen if PerformAllValidations returns correctly
                {
                    _log.Error("Validation pipeline failed, but ErrorResult was not in expected format for logging the TransactionResponse object.");
                }
                return errorResult;
            }
            
            _log.Info("All validations passed. Calculating discounts.");
            var (calculatedDiscount, finalAmount) = CalculateDiscountDetails(request.TotalAmount);

            // Return Success Response
            var successResponse = new TransactionResponse
            {
                Result = 1,
                TotalAmount = request.TotalAmount,
                TotalDiscount = calculatedDiscount,
                FinalAmount = finalAmount
            };
            _log.Info($"Processing successful. Response: {SerializeForLog(successResponse)}");
            return Ok(successResponse);
        }

        // Helper method for IsPrime 
        private bool IsPrime(long number)
        {
            if (number <= 1) return false;
            if (number <= 3) return true;
            if (number % 2 == 0 || number % 3 == 0) return false;
            for (long i = 5; i * i <= number; i = i + 6)
            {
                if (number % i == 0 || number % (i + 2) == 0)
                    return false;
            }
            return true;
        }

        // Helper method for CalculateDiscountDetails 
        private (long calculatedTotalDiscountInCents, long finalAmountInCents) CalculateDiscountDetails(long totalAmountInCents)
        {
            _log.Debug($"Calculating discount for TotalAmountInCents: {totalAmountInCents}");
            double baseDiscountPercent = 0;
            if (totalAmountInCents < 20000) { baseDiscountPercent = 0; }
            else if (totalAmountInCents <= 50000) { baseDiscountPercent = 0.05; }
            else if (totalAmountInCents <= 80000) { baseDiscountPercent = 0.07; }
            else if (totalAmountInCents <= 120000) { baseDiscountPercent = 0.10; }
            else { baseDiscountPercent = 0.15; }

            double conditionalDiscountPercent = 0;
            if (totalAmountInCents > 50000 && IsPrime(totalAmountInCents)) { conditionalDiscountPercent += 0.08; }
            
            long myrValueForDigitCheck = totalAmountInCents / 100;
            if (totalAmountInCents > 90000 && (myrValueForDigitCheck % 10 == 5)) { conditionalDiscountPercent += 0.10; }

            double totalCalculatedDiscountPercent = baseDiscountPercent + conditionalDiscountPercent;
            double actualAppliedDiscountPercent = Math.Min(totalCalculatedDiscountPercent, 0.20);

            long calculatedTotalDiscountInCents = (long)Math.Round(totalAmountInCents * actualAppliedDiscountPercent);
            long finalAmountInCents = totalAmountInCents - calculatedTotalDiscountInCents;
            _log.Debug($"Discount calculation: BaseDisc%={baseDiscountPercent}, CondDisc%={conditionalDiscountPercent}, AppliedDisc%={actualAppliedDiscountPercent}, TotalDiscCents={calculatedTotalDiscountInCents}, FinalAmountCents={finalAmountInCents}");
            return (calculatedTotalDiscountInCents, finalAmountInCents);
        }

        // IsValidSignature method 
        private bool IsValidSignature(SubmitTransactionRequest request, DateTime parsedRequestTimestampUtc)
        {
            _log.Debug($"IsValidSignature called for PartnerKey: '{request.PartnerKey}', PartnerRefNo: '{request.PartnerRefNo}'");
            string sigTimestampValue = parsedRequestTimestampUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

            StringBuilder stringToSignBuilder = new StringBuilder();
            stringToSignBuilder.Append(sigTimestampValue);
            stringToSignBuilder.Append(request.PartnerKey);
            stringToSignBuilder.Append(request.PartnerRefNo);
            stringToSignBuilder.Append(request.TotalAmount.ToString(CultureInfo.InvariantCulture));
            stringToSignBuilder.Append(request.PartnerPassword); // This is the Base64 encoded password from the request

            string stringToSign = stringToSignBuilder.ToString();
            _log.Debug($"IsValidSignature - String to Sign: '{stringToSign}'");

            string lowercaseHexHash;
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(stringToSign);
                byte[] hashedBytes = sha256.ComputeHash(inputBytes);
                lowercaseHexHash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLowerInvariant();
            }
            _log.Debug($"IsValidSignature - Lowercase Hex Hash: '{lowercaseHexHash}'");

            string calculatedSig;
            try
            {
                calculatedSig = Convert.ToBase64String(Encoding.UTF8.GetBytes(lowercaseHexHash));
            }
            catch (Exception ex)
            {
                _log.Error("IsValidSignature - Exception during Base64 encoding of hex hash.", ex);
                return false;
            }
            // This log line is crucial for debugging signature issues.
            _log.Info($"IsValidSignature - Calculated Signature (Base64 of Hex Hash): {calculatedSig} | Received Sig: {request.Sig}");
            Console.WriteLine($"Calculated Signature: {calculatedSig} ");

            if (string.IsNullOrEmpty(request.Sig)) 
            {
                _log.Warn("IsValidSignature - Received signature (request.Sig) is null or empty.");
                return false;
            }
            bool isValid = calculatedSig == request.Sig;
            if (!isValid) {
                _log.Warn("IsValidSignature - Signature MISMATCH.");
            }
            return isValid;
        }

        [HttpGet("ping")]// For Testing Purpose
        public IActionResult GetTimeStamp()
        {
            _log.Info("Ping endpoint was HIT.");
            // Output current server time in ISO 8601 format with 7 decimal places for seconds
            string currentServerTimeFormatted = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            Console.WriteLine($"SERVER_TIME_INIT (yyyyMMddHHmmss UTC): {currentServerTimeFormatted}");
            string currentServerTimeISO = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
            Console.WriteLine($"SERVER_TIME_INIT (ISO 8601 UTC): {currentServerTimeISO}");
            return Ok(new TransactionResponse { Result = 1, ResultMessage = $"SERVER TIME NOW: {currentServerTimeFormatted} & {currentServerTimeISO}" });
        }
    }
}
