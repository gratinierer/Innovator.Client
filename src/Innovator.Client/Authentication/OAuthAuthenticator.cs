using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Innovator.Client
{
  internal class OAuthAuthenticator : IAuthenticator
  {
    private readonly ExplicitHashCredentials _hashCred;
    private readonly HttpClient _service;
    private readonly OAuthConfig _config;
    private IPromise<TokenCredentials> _tokenCreds;

    public OAuthAuthenticator(HttpClient service, OAuthConfig config, ICredentials creds)
    {
      _config = config;
      _service = service;
      if (creds is TokenCredentials token)
        _tokenCreds = Promises.Resolved(token);
      else
        _hashCred = HashCredentials(creds);
    }

    private IPromise<TokenCredentials> GetCredentials(bool async)
    {
      var req = new HttpRequest
      {
        Content = new SimpleContent(new Dictionary<string, string>()
        {
          { "client_id", "IOMApp" },
          { "grant_type", "password" },
          { "scope", "Innovator" },
          { "username", Uri.EscapeDataString(_hashCred.Username) },
          { "database", Uri.EscapeDataString(_hashCred.Database) },
          { "password", Uri.EscapeDataString(_hashCred.PasswordHash) },
        }.Select(k => k.Key + "=" + k.Value).GroupConcat("&"), "application/x-www-form-urlencoded")
      };
      req.Headers.Add("Accept", "application/json");
      return _service.PostPromise(_config.TokenEndpoint, async, req, new LogData(4
          , "Innovator: Get OAuth token"
          , Factory.LogListener)
        {
          { "database", _hashCred.Database },
          { "url", _config.TokenEndpoint },
          { "user_name", _hashCred.Username },
        })
        .Convert(r => new TokenCredentials(r.AsStream, _hashCred.Database))
        .Fail(e =>
        {
          if (e is HttpException httpEx)
          {
            var faultCode = default(string);
            var faultstring = default(string);
            using (var reader = new Json.Embed.JsonTextReader(httpEx.Response.AsStream))
            {
              foreach (var kvp in reader.Flatten())
              {
                switch (kvp.Key)
                {
                  case "$.error":
                    faultCode = kvp.Value?.ToString();
                    break;
                  case "$.error_description":
                    faultstring = kvp.Value?.ToString();
                    break;
                }
              }
            }

            throw ElementFactory.Local.FromXml(@"<SOAP-ENV:Envelope xmlns:SOAP-ENV='http://schemas.xmlsoap.org/soap/envelope/' xmlns:i18n='http://www.aras.com/I18N'>
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultcode>@0</faultcode>
      <faultstring>@1</faultstring>
      <faultactor>OAuthServer</faultactor>
      <detail>unknown error</detail>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>", faultCode, faultstring).Exception;
          }
        });
    }

    private IPromise<TokenCredentials> ValidCredentials(bool async)
    {
      if (_tokenCreds == null)
      {
        _tokenCreds = GetCredentials(async);
        return _tokenCreds;
      }
      else
      {
        return _tokenCreds.Continue(c =>
        {
          if (c.Expires <= DateTime.UtcNow)
          {
            _tokenCreds = GetCredentials(async);
            return _tokenCreds;
          }
          else
          {
            return Promises.Resolved(c);
          }
        });
      }
    }

    public IPromise<IEnumerable<KeyValuePair<string, string>>> GetAuthHeaders(bool async)
    {
      return ValidCredentials(async)
        .Convert(c => (IEnumerable<KeyValuePair<string, string>>)new[]
        {
          new KeyValuePair<string, string>("Authorization", c.TokenType + " " + c.AccessToken)
        });
    }

    public ExplicitHashCredentials HashCredentials(ICredentials credentials)
    {
      if (credentials is ExplicitHashCredentials hashCred)
        return hashCred;
      else if (credentials is ExplicitCredentials passCred)
        return new ExplicitHashCredentials(passCred.Database, passCred.Username, ElementFactory.Local.CalcMd5(passCred.Password));
      else
        throw new NotSupportedException($"Credentials of type {credentials.GetType().Name} are not supported");
    }

    public IPromise<ExplicitHashCredentials> HashCredentials(ICredentials credentials, bool async)
    {
      return Promises.Resolved(HashCredentials(credentials));
    }
  }
}
