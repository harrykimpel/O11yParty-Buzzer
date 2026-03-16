namespace O11yPartyBuzzer.Services;

public interface INewRelicEventPublisher
{
    Task PublishBuzzAsync(string teamName, CancellationToken cancellationToken = default);
}
