using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace TenisWhatsAppAdmin.Authentication
{
    public class AuthStateProvider : AuthenticationStateProvider
    {
        private const string SessionKey = "authUser";
        private readonly ProtectedSessionStorage _sessionStorage;

        // Hardcoded credentials - CHANGE BEFORE PRODUCTION
        private const string HardcodedUsername = "admin";
        private const string HardcodedPassword = "admin";

        public AuthStateProvider(ProtectedSessionStorage sessionStorage)
        {
            _sessionStorage = sessionStorage;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var saved = await _sessionStorage.GetAsync<string>(SessionKey);
                if (saved.Success && !string.IsNullOrWhiteSpace(saved.Value))
                {
                    var username = saved.Value;
                    var identity = new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Name, username),
                        new Claim(ClaimTypes.Role, "Admin")
                    }, authenticationType: "Custom");

                    var user = new ClaimsPrincipal(identity);
                    return new AuthenticationState(user);
                }
            }
            catch
            {
                // ignore and treat as logged out
            }

            // not authenticated
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            // Very simple hardcoded check
            if (username == HardcodedUsername && password == HardcodedPassword)
            {
                await _sessionStorage.SetAsync(SessionKey, username);

                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.Role, "Admin")
                }, authenticationType: "Custom");

                var user = new ClaimsPrincipal(identity);
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
                return true;
            }

            return false;
        }

        public async Task LogoutAsync()
        {
            await _sessionStorage.DeleteAsync(SessionKey);
            var anon = new ClaimsPrincipal(new ClaimsIdentity());
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(anon)));
        }
    }
}
