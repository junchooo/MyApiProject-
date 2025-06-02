using System.ComponentModel.DataAnnotations;

namespace TransactionAPI.Models 
{
    public class ItemDetail
    {
        [Required(ErrorMessage = "PartnerItemRef is required.")]
        [StringLength(50)]
        public string? PartnerItemRef { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100)]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Qty is required.")]
        [Range(1, 5, ErrorMessage = "Quantity must be between 1 and 5.")]
        public int Qty { get; set; } 

        [Required(ErrorMessage = "UnitPrice is required.")]
        [Range(1, long.MaxValue, ErrorMessage = "UnitPrice must be a positive value.")]
        public long UnitPrice { get; set; } 
    }
}