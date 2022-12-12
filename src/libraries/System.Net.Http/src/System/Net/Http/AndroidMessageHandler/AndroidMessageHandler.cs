// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http;

public class AndroidMessageHandler : HttpMessageHandler
{
    public bool UseCookies { get; set; } = true;
    public CookieContainer CookieContainer { get; set; } = new();

    public ICredentials? DefaultProxyCredentials { get; set; }
    public bool UseDefaultCredentials { get; set; }
    public ICredentials? Credentials { get; set; }
    public bool PreAuthenticate { get; set; }

    public bool UseProxy { get; set; } = true;
    public IWebProxy? Proxy { get; set; }

    public int MaxConnectionsPerServer { get; set; } = int.MaxValue; // not implemented in Xamarin
    public int MaxResponseHeadersLength { get; set; } = 64; // Units in K (1024) bytes.

    public ClientCertificateOption ClientCertificateOptions { get; set; }
    public X509CertificateCollection ClientCertificates { get; set; } = new();
    public Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateCustomValidationCallback { get; set; }
    public bool CheckCertificateRevocationList { get; set; }

    private static readonly Lazy<SslProtocols> s_androidSupportedSslProtocols = new Lazy<SslProtocols>(Interop.AndroidCrypto.SSLGetSupportedProtocols);
    private SslProtocols? _sslProtocols;
    public SslProtocols SslProtocols
    {
        get => _sslProtocols ??= s_androidSupportedSslProtocols.Value;
        set => _sslProtocols = value;
    }

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
    public DecompressionMethods AutomaticDecompression { get; set; }


    public bool SupportsAutomaticDecompression { get; } = true;
    public bool SupportsProxy { get; } = true;
    public bool SupportsRedirectConfiguration { get; } = true;

    public bool AllowAutoRedirect { get; set; }
    private int _maxAutomaticRedirections = 50;
    public int MaxAutomaticRedirections
    {
        get => _maxAutomaticRedirections;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            _maxAutomaticRedirections = value;
        }
    }

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromHours(24);

    private static bool NegotiateAuthenticationIsEnabled
        => AppContext.TryGetSwitch("Xamarin.Android.Net.UseNegotiateAuthentication", out bool isEnabled) && isEnabled;

    protected internal override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpRequestMessage? nextRequest = request;
        HttpResponseMessage? response;
        int requestCounter = 0;

        // TODO logging

        do
        {
            requestCounter++;
            response = await MakeRequest(nextRequest, cancellationToken).ConfigureAwait(false);
            ProcessCookies(response, nextRequest.RequestUri!);
            nextRequest = CreateRedirectRequest(request, response);
        } while (nextRequest is not null && requestCounter < MaxAutomaticRedirections + 1);

        return response;
    }

    private async Task<HttpResponseMessage> MakeRequest(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        return await Task.Run(() => MakeRequestCore(request, cancellationToken)).ConfigureAwait(false);
    }

    private HttpResponseMessage MakeRequestCore(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = Interop.Http.MakeRequest(
            request.RequestUri!,
            request.Method,
            CollectHeaders(request),
            handler: this,
            cancellationToken);

        response.RequestMessage = request;

        return response;
    }

    private void ProcessCookies(HttpResponseMessage response, Uri uri)
    {
        IEnumerable<string>? cookieHeaderValue;
        if (!UseCookies || CookieContainer == null || !response.Headers.TryGetValues("Set-Cookie", out cookieHeaderValue) || cookieHeaderValue == null)
        {
            // TODO log
            return;
        }

        try
        {
            CookieContainer.SetCookies(uri, string.Join(",", cookieHeaderValue));
        }
        catch
        {
            // We don't want to terminate the response because of a bad cookie, hence just reporting
            // the issue. We might consider adding a virtual method to let the user handle the
            // issue, but not sure if it's really needed. Set-Cookie header will be part of the
            // header collection so the user can always examine it if they spot an error.

            // TODO log
        }
    }

    private Dictionary<string, string> CollectHeaders(HttpRequestMessage request)
    {
        var accumulatedHeaders = new Dictionary<string, string>();

        if (request.Content is HttpContent content)
            AddHeaders(accumulatedHeaders, content.Headers);

        AddHeaders(accumulatedHeaders, request.Headers);

        // TODO accept encoding

        if (UseCookies && CookieContainer != null)
        {
            string cookieHeaderValue = CookieContainer.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookieHeaderValue))
            {
                accumulatedHeaders.Add("Cookie", cookieHeaderValue);
            }
        }

        // TODO authentication

        return accumulatedHeaders;

        static void AddHeaders(Dictionary<string, string> output, HttpHeaders httpHeaders)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in httpHeaders)
            {
                output.Add(header.Key, string.Join(",", header.Value ?? Array.Empty<string>()));
            }
        }
    }

#pragma warning disable IDE0060
    private HttpRequestMessage? CreateRedirectRequest(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (!AllowAutoRedirect)
            return null;

        // TODO

        return null;
    }
#pragma warning restore IDE0060

    private Func<IntPtr, IntPtr>? _configureCustomSSLSocketFactoryHook;
    private Func<IntPtr, IntPtr>? _configureKeyStoreHook;
    private Func<IntPtr, IntPtr>? _configureTrustManagerFactoryHook;

    protected internal virtual IntPtr ConfigureKeyStore(IntPtr keyStore)
    {
        if (_configureKeyStoreHook is null)
        {
            Log($"setting up fallback keysore hook");
            _configureKeyStoreHook = (handle) => handle;
        }

        Log($"ConfigureKeyStore: keyStore={keyStore}");
        return _configureKeyStoreHook(keyStore);
    }

    protected internal virtual IntPtr ConfigureTrustManagerFactory(IntPtr keyStore)
    {
        if (_configureTrustManagerFactoryHook is null)
        {
            Log($"setting up fallback keysore hook");
            _configureTrustManagerFactoryHook = (handle) => IntPtr.Zero;
        }

        Log($"ConfigureKeyStore: keyStore={keyStore}");
        return _configureTrustManagerFactoryHook(keyStore);
    }

    protected internal virtual IntPtr ConfigureKeyManagerFactory(IntPtr keyStore)
    {
        Log($"ConfigureKeyManagerFactory: keyStore={keyStore}");
        return IntPtr.Zero;
    }

    protected internal virtual IntPtr ConfigureCustomSSLSocketFactory(IntPtr httpsUrlConnection)
    {
        if (_configureCustomSSLSocketFactoryHook is null)
        {
            Log($"setting up fallback ConfigureCustomSSLSocketFactory hook");
            _configureCustomSSLSocketFactoryHook = (handle) => IntPtr.Zero;
        }

        Log($"ConfigureCustomSSLSocketFactory: httpsUrlConnection={httpsUrlConnection}");
        return _configureCustomSSLSocketFactoryHook(httpsUrlConnection);
    }

    private static void Log(string msg)
        => Interop.Logcat.AndroidLogPrint(Interop.Logcat.LogLevel.Info, "DOTNET", msg);
}
