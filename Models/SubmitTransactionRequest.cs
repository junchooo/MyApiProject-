using System.Collections.Generic; // For List<>
using System.ComponentModel.DataAnnotations; // For validation attributes

namespace TransactionAPI.Models // Or your actual namespace
{
    public class SubmitTransactionRequest
    {
       
        [Required(ErrorMessage = "PartnerKey is required.")]
        [StringLength(50)]
        public string? PartnerKey { get; set; } 

        
        [Required(ErrorMessage = "PartnerRefNo is required.")]
        [StringLength(50)]
        public string? PartnerRefNo { get; set; } 


        [Required(ErrorMessage = "PartnerPassword is required.")]
        [StringLength(50)] 
        public string? PartnerPassword { get; set; } 

       
        [Required(ErrorMessage = "TotalAmount is required.")]
        [Range(1, long.MaxValue, ErrorMessage = "TotalAmount must be a positive value.")]
        public long TotalAmount { get; set; } 

       
        public List<ItemDetail>? Items { get; set; } 

       
        [Required(ErrorMessage = "Timestamp is required.")]

        public string? Timestamp { get; set; } 


        [Required(ErrorMessage = "Sig is required.")]
        public string? Sig { get; set; }
    }
}