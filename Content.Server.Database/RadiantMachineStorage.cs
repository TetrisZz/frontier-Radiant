using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Content.Server.Database;

[Table("radiant_machine_storage")]
public sealed class RadiantMachineStorage
{
    [Key, MaxLength(128)] public string StorageId { get; set; } = string.Empty;
    [Required] public string Kind { get; set; } = string.Empty;
    [Required] public string Payload { get; set; } = string.Empty;
    public long Revision { get; set; }
}
