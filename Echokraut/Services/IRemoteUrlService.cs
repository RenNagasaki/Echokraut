using Echokraut.DataClasses;

namespace Echokraut.Services;

public interface IRemoteUrlService
{
    RemoteUrlsData Urls { get; }
}
