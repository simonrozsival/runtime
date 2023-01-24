// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace System.Net.Http
{
    public partial class HttpClientHandler : HttpMessageHandler
    {
        private AndroidMessageHandler Handler => _nativeHandler as AndroidMessageHandler
            ?? throw new InvalidOperationException($"The native handler hasn't been initialized with an instance of {nameof(AndroidMessageHandler)}.");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ICredentials? GetDefaultProxyCredentials()
            => Handler.DefaultProxyCredentials;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDefaultProxyCredentials(ICredentials? value)
            => Handler.DefaultProxyCredentials = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetMaxConnectionsPerServer()
            => Handler.MaxConnectionsPerServer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetMaxConnectionsPerServer(int value)
            => Handler.MaxConnectionsPerServer = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetMaxResponseHeadersLength()
            => Handler.MaxResponseHeadersLength;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetMaxResponseHeadersLength(int value)
            => Handler.MaxResponseHeadersLength = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ClientCertificateOption GetClientCertificateOptions()
            => Handler.ClientCertificateOptions;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetClientCertificateOptions(ClientCertificateOption value)
            => Handler.ClientCertificateOptions = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private X509CertificateCollection GetClientCertificates()
            => Handler.ClientCertificates;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? GetServerCertificateCustomValidationCallback()
            => Handler.ServerCertificateCustomValidationCallback;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetServerCertificateCustomValidationCallback(Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? value)
            => Handler.ServerCertificateCustomValidationCallback = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetCheckCertificateRevocationList()
            => Handler.CheckCertificateRevocationList;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCheckCertificateRevocationList(bool value)
            => Handler.CheckCertificateRevocationList = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SslProtocols GetSslProtocols()
            => Handler.SslProtocols;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetSslProtocols(SslProtocols value)
            => Handler.SslProtocols = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IDictionary<string, object?> GetProperties()
            => Handler.Properties;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetSupportsAutomaticDecompression()
            => Handler.SupportsAutomaticDecompression;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetSupportsProxy()
            => Handler.SupportsProxy;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetSupportsRedirectConfiguration()
            => Handler.SupportsRedirectConfiguration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private DecompressionMethods GetAutomaticDecompression()
            => Handler.AutomaticDecompression;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetAutomaticDecompression(DecompressionMethods value)
            => Handler.AutomaticDecompression = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetUseProxy()
            => Handler.UseProxy;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUseProxy(bool value)
            => Handler.UseProxy = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IWebProxy? GetProxy()
            => Handler.Proxy;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetProxy(IWebProxy value)
            => Handler.Proxy = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetPreAuthenticate()
            => Handler.PreAuthenticate;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetPreAuthenticate(bool value)
            => Handler.PreAuthenticate = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetMaxAutomaticRedirections()
            => Handler.MaxAutomaticRedirections;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetMaxAutomaticRedirections(int value)
            => Handler.MaxAutomaticRedirections = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetUseCookies()
            => Handler.UseCookies;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUseCookies(bool value)
            => Handler.UseCookies = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private CookieContainer GetCookieContainer()
            => Handler.CookieContainer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCookieContainer(CookieContainer value)
            => Handler.CookieContainer = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetAllowAutoRedirect()
            => Handler.AllowAutoRedirect;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetAllowAutoRedirect(bool value)
            => Handler.AllowAutoRedirect = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ICredentials? GetCredentials()
            => Handler.Credentials;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCredentials(ICredentials? value)
            => Handler.Credentials = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static HttpMessageHandler CreateNativeHandler()
            => AndroidMessageHandlerFactory.CreateNativeHandler();
    }
}
