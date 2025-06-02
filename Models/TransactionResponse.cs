using System.Text.Json.Serialization; // Required for JsonIgnore

namespace TransactionAPI.Models // Ensure this namespace matches your project
{
    public class TransactionResponse
    {

        public int Result { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? TotalAmount { get; set; }

 
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? TotalDiscount { get; set; }


        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? FinalAmount { get; set; }


        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ResultMessage { get; set; }

    }
}