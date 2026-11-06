namespace TrainingManagement.Api.Models;
public sealed class ListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 500;
    public string? Search { get; set; }
    public string? Department { get; set; }
    public string? Category { get; set; }
    public string? Status { get; set; }
    public bool? IsActive { get; set; }
    public int? CourseId { get; set; }
    public int Offset => (Math.Clamp(Page, 1, 100000) - 1) * Limit;
    public int Limit => Math.Clamp(PageSize, 1, 500);
}
