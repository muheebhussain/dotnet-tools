public interface IAuthTokenService
{
       
}

public interface IAuthTokenService<TService> : IAuthTokenService
{
    Task<string> GetAuthTokenAsync();
}

public interface IAuthResponse
{
    public string? AccessToken { get; set; }

    public bool? IsValid { get; }
        
    public string ExpiresIn { get; set; }
}


public class AuthTokenCacheManager
{
    private readonly ConcurrentDictionary<string, IAuthResponse> _cache = new();

    public void AddOrUpdate(string clientId, IAuthResponse authResponse)
    {
        _cache.TryRemove(clientId, out _);
        _cache.TryAdd(clientId, authResponse);
    }

    public string? GetToken(string clientId)
    {
        _cache.TryGetValue(clientId, out var tokenCacheEntry);

        return tokenCacheEntry?.IsValid == true ? tokenCacheEntry.AccessToken : null;

    }
}

public class AuthHandler<TService> : DelegatingHandler where TService : class, IAuthTokenService<TService>
{
    private readonly IAuthTokenService<TService> _authTokenService;

    public AuthHandler(IAuthTokenService<TService> authTokenService) { _authTokenService = authTokenService; }
        
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authTokenService.GetAuthTokenAsync();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}

public class ApiAuthOption
{
    public string AuthenticateDpdSecret { get; set; } = null!;

    public string Authority { get; set; } = "https://login.microsoftonline.com";

    public string ClientId { get; set; } = null!;

    public string ClientSecret { get; set; } = null!;

    public string TenantId { get; set; } = null!;

    public string AuthenticateResource { get; set; } = null!;

    public string? ResourceId { get; set; }

    public string? Scope { get; set; }
}
public class AuthTokenService<TService> : IAuthTokenService<TService> where TService : class, IAuthTokenService
    {
        private readonly ApiAuthOption _apiAuthOption;
        private IConfidentialClientApplication _client;
        public AuthTokenService(IOptions<ApiAuthOption> apiAuthOptions)
        {
            _apiAuthOption = apiAuthOptions.Value;
        }

        public AuthTokenService(ApiAuthOption authOption)
        {
            
        }
        
        public virtual async Task<string> GetAuthTokenAsync()
        {
            var authorityEndpoint = _apiAuthOption.Authority.EndsWith("/")
                ? _apiAuthOption.Authority.Remove(_apiAuthOption.Authority.Length, 1)
                : _apiAuthOption.Authority;
            
            var authority = $"{authorityEndpoint}/{_apiAuthOption.TenantId}";
            
            _client ??= ConfidentialClientApplicationBuilder.Create(_apiAuthOption.ClientId)
                .WithClientSecret(_apiAuthOption.AuthenticateDpdSecret)
                .WithAuthority(authority)
                .Build();

            _client.AddInMemoryTokenCache();
            
            var authResult = await _client
                .AcquireTokenForClient(new[] { $"{_apiAuthOption.AuthenticateResource}/.default" })
                .ExecuteAsync();

            return authResult.CreateAuthorizationHeader();
        }
    }
}
