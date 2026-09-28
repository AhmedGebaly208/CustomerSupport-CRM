namespace CustomerSupportCRM.Domain.Enums;

/// <summary>Where an article stands editorially (PDF area 6).</summary>
public enum ArticleStatus
{
    /// <summary>Being written. Never visible outside the editor.</summary>
    Draft = 0,

    Published = 1,

    /// <summary>Withdrawn but kept. Existing links still resolve for agents so an old ticket
    /// referencing it still makes sense; customers no longer see it.</summary>
    Archived = 2
}
