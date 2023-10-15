using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace stage_api.Models
{
    public class UploadedFile
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; } 
        public string fileName { get; set; }
        public string fileSize { get; set; }
        public string destinationTable { get; set; }
        public DateTime uploadDate { get; set; }
        public string uploadUser { get; set; }

        public string? zipFileName { get; set; }
        public DateTime? deletedAt { get; set; } 
    }
}
