namespace UmbracoSample.Domain.Leads;

/// <summary>Field limits from threat model 0001, decision D3. Anything longer is rejected, never truncated.</summary>
public static class LeadLimits
{
    public const int Name = 100;
    public const int Email = 254;
    public const int EmailLocalPart = 64;
    public const int Company = 100;
    public const int Message = 2000;
    public const int ConsentVersion = 32;
}
