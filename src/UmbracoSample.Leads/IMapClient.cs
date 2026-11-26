namespace UmbracoSample.Leads;

/// <summary>Sends a lead to the MAP. <see cref="MapClient"/> is the only implementation; the seam lets the form handler be tested alone.</summary>
public interface IMapClient
{
    Task<MapOutcome> SubmitAsync(MapSubmission submission, CancellationToken cancellationToken);
}
