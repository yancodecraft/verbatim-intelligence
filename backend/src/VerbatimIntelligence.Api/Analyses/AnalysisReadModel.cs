using Microsoft.EntityFrameworkCore;

using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Analyses;

/// <summary>
/// The read projection of an analysis's results, shared by the owner's
/// detail endpoint and the public shared report: themes in the volume order
/// the worker persisted, citations resolved by foreign key, and the
/// unclassified count so no loss is silent.
/// </summary>
public static class AnalysisReadModel
{
    /// <summary>
    /// Themes in position order; a cited text is the original verbatim row
    /// resolved by its foreign key — never a stored copy.
    /// </summary>
    public static Task<List<ThemeResponse>> ThemesAsync(
        AppDbContext db, Guid analysisId, CancellationToken cancellationToken) =>
        db.Themes
            .Where(theme => theme.AnalysisId == analysisId)
            .OrderBy(theme => theme.Position)
            .Select(theme => new ThemeResponse(
                theme.Name,
                theme.Synthesis,
                db.ThemeVerbatims.Count(tv => tv.ThemeId == theme.Id),
                db.ThemeVerbatims
                    .Where(tv => tv.ThemeId == theme.Id && tv.Rank != null)
                    .OrderBy(tv => tv.Rank)
                    .Join(
                        db.Verbatims,
                        tv => tv.VerbatimId,
                        verbatim => verbatim.Id,
                        (tv, verbatim) => new RepresentativeResponse(
                            verbatim.Position, verbatim.Text))
                    .ToList()))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// No silent loss: verbatims no step attached to any theme are counted,
    /// never inferred from the per-theme counts (a verbatim may support
    /// several themes). Always computed, never stored.
    /// </summary>
    public static Task<int> UnclassifiedCountAsync(
        AppDbContext db, Guid analysisId, CancellationToken cancellationToken) =>
        db.Verbatims
            .Where(verbatim => verbatim.AnalysisId == analysisId
                && !db.ThemeVerbatims.Any(tv => tv.VerbatimId == verbatim.Id))
            .CountAsync(cancellationToken);
}