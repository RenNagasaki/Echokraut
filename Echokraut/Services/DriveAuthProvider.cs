using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Echokraut.Services;

/// <summary>
/// Owns the Google OAuth (PKCE installed-app) flow and hands out an authorized
/// <see cref="DriveService"/>. Split out of GoogleDriveSyncService (SRP: authentication).
/// Credentials live in the <c>DriveAuthProvider.Secrets.cs</c> partial.
/// </summary>
internal sealed partial class DriveAuthProvider
{
    public async Task<DriveService> CreateDriveServiceAsync()
    {
        var scopes = new[]
        {
            DriveService.Scope.DriveReadonly,
            DriveService.Scope.DriveFile
        };

        var dataStore = new FileDataStore("Echokraut.Auth", false);

        var flow = new PkceGoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = CLIENTID,
                ClientSecret = CLIENTSECRET
            },
            Scopes = scopes,
            DataStore = dataStore
        });

        var credential = await new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver())
                             .AuthorizeAsync("Echokraut", CancellationToken.None);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Echokraut"
        });
    }
}
