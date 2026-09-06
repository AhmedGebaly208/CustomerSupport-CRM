namespace CustomerSupportCRM.Application.Common.Models;

/// <summary>Shared paging/sorting inputs. PageSize is clamped rather than validated away so
/// a careless client cannot ask for the whole ticket table.</summary>
public abstract class PagedQuery
{
    private const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 20,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }

    public int Skip => (Page - 1) * PageSize;
}
