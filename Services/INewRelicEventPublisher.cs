namespace O11yPartyBuzzer.Services;

public interface INewRelicEventPublisher
{
    Task PublishBuzzAsync(string teamName, CancellationToken cancellationToken = default);

    Task PublishLeadCaptureAsync(
        string firstName,
        string lastName,
        string businessEmailAddress,
        string companyName,
        string jobTitle,
        string country,
        CancellationToken cancellationToken = default);
}
