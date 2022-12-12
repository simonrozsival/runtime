// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static partial class Interop
{
    internal static partial class Http
    {
        private enum PAL_HttpMakeRequestResult
        {
            Success = 0,
            Error = 1,
            Timeout = 2,
            Cancelled = 3,
        }

        private unsafe struct StringsList
        {
            public byte** Values;
            public nuint* Lengths;

            public unsafe StringsList(IEnumerable<string> strings, int count)
            {
                Values = (byte**)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(byte*));
                Lengths = (nuint*)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(nuint));

                int i = 0;
                foreach (var str in strings)
                {
                    int length = Encoding.UTF8.GetByteCount(str);
                    Lengths[i] = (nuint)length;
                    Values[i] = (byte*)NativeMemory.Alloc((nuint)length + 1);

                    int bytesWritten = Encoding.UTF8.GetBytes(str, new Span<byte>(Values[i], length));
                    Debug.Assert(bytesWritten == length);

                    Values[i][bytesWritten] = (byte)'\0';

                    i++;
                }
            }

            public void Free(nuint count)
            {
                if (Values is not null)
                {
                    for (nuint i = 0; i < count; i++)
                    {
                        if (Values[i] is not null)
                        {
                            NativeMemory.Free(Values[i]);
                        }
                    }

                    NativeMemory.Free(Values);
                }

                if (Lengths is not null)
                {
                    NativeMemory.Free(Lengths);
                }
            }

            public string DecodeItem(int index)
            {
                var span = new ReadOnlySpan<byte>(Values[index], (int)Lengths[index]);
                return Encoding.UTF8.GetString(span);
            }
        }

        private unsafe struct Headers : IDisposable
        {
            public nuint Count;
            public StringsList Names;
            public StringsList Values;

            private static readonly HashSet<string> s_knownContentHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Allow",
                "Content-Disposition",
                "Content-Encoding",
                "Content-Language",
                "Content-Length",
                "Content-Location",
                "Content-MD5",
                "Content-Range",
                "Content-Type",
                "Expires",
                "Last-Modified"
            };

            public Headers(IDictionary<string, string> headers)
            {
                Count = (nuint)headers.Count;
                Names = new StringsList(headers.Keys, headers.Count);
                Values = new StringsList(headers.Values, headers.Count);
            }

            public void Dispose()
            {
                Names.Free(Count);
                Values.Free(Count);
            }

            public void CopyTo(HttpResponseMessage response)
            {
                for (int i = 0; i < (int)Count; i++)
                {
                    string name = Names.DecodeItem(i);
                    string value = Values.DecodeItem(i);

                    HttpHeaders targetHeaders = s_knownContentHeaders.Contains(name)
                        ? response.Content.Headers
                        : response.Headers;

                    targetHeaders.TryAddWithoutValidation(name, value);
                }
            }
        }

        private unsafe struct Request : IDisposable
        {
            public byte* Method;
            public byte* Uri;
            public Headers Headers;

            public void Dispose()
            {
                Headers.Dispose();
            }
        }

        private unsafe struct Proxy
        {
            public bool UseProxy;
            public byte* Host;
            public int Port;
        }

        private unsafe struct Hooks : IDisposable
        {
            public GCHandle AndroidMessageHandler;
            public delegate* unmanaged<IntPtr, IntPtr, IntPtr> ConfigureKeyStore;
            public delegate* unmanaged<IntPtr, IntPtr, IntPtr> ConfigureTrustManagerFactory;
            public delegate* unmanaged<IntPtr, IntPtr, IntPtr> ConfigureKeyManagerFactory;
            public delegate* unmanaged<IntPtr, IntPtr, IntPtr> ConfigureCustomSSLSocketFactory;

            public Hooks(AndroidMessageHandler handler)
            {
                AndroidMessageHandler = GCHandle.Alloc(handler);
                ConfigureKeyStore = &ConfigureKeyStoreCallback;
                ConfigureTrustManagerFactory = &ConfigureTrustManagerFactoryCallback;
                ConfigureKeyManagerFactory = &ConfigureKeyManagerFactoryCallback;
                ConfigureCustomSSLSocketFactory = &ConfigureCustomSSLSocketFactoryCallback;
            }

            public void Dispose()
            {
                AndroidMessageHandler.Free();
            }

            [UnmanagedCallersOnly]
            private static unsafe IntPtr ConfigureKeyStoreCallback(IntPtr handlerPtr, IntPtr keyStore)
                => GetHandler(handlerPtr).ConfigureKeyStore(keyStore);

            [UnmanagedCallersOnly]
            private static unsafe IntPtr ConfigureTrustManagerFactoryCallback(IntPtr handlerPtr, IntPtr keyStore)
                => GetHandler(handlerPtr).ConfigureTrustManagerFactory(keyStore);

            [UnmanagedCallersOnly]
            private static unsafe IntPtr ConfigureKeyManagerFactoryCallback(IntPtr handlerPtr, IntPtr keyStore)
                => GetHandler(handlerPtr).ConfigureKeyManagerFactory(keyStore);

            [UnmanagedCallersOnly]
            private static unsafe IntPtr ConfigureCustomSSLSocketFactoryCallback(IntPtr handlerPtr, IntPtr httpsUrlConnection)
                => GetHandler(handlerPtr).ConfigureCustomSSLSocketFactory(httpsUrlConnection);

            private static AndroidMessageHandler GetHandler(IntPtr handlerPtr)
                => (AndroidMessageHandler)GCHandle.FromIntPtr(handlerPtr).Target!;
        }

        private unsafe struct Configuration : IDisposable
        {
            public Proxy Proxy;
            public int ConnectTimeoutMilliseconds;
            public int ReadTimeoutMilliseconds;
            public IntPtr CancellationToken;
            public delegate* unmanaged<IntPtr, bool> IsCancellationRequested;
            public Hooks Hooks;

            public Configuration(CancellationToken cancellationToken)
            {
                CancellationToken = (IntPtr)GCHandle.Alloc(cancellationToken);
                IsCancellationRequested = &IsCancellationRequestedCallback;
            }

            public void Dispose()
            {
                GCHandle.FromIntPtr(CancellationToken).Free();
                Hooks.Dispose();
            }

            [UnmanagedCallersOnly]
            private static unsafe bool IsCancellationRequestedCallback(IntPtr tokenPtr)
            {
                CancellationToken? token = (CancellationToken?)GCHandle.FromIntPtr(tokenPtr).Target;
                return token?.IsCancellationRequested ?? false;
            }
        }

        private unsafe struct Response
        {
            public HttpStatusCode StatusCode;
            public byte* ConnectionUri;
            public nuint ConnectionUriLength;
            public Headers Headers;

            public Uri DecodeConnectionUri()
            {
                var span = new ReadOnlySpan<byte>(ConnectionUri, (int)ConnectionUriLength);
                return new Uri(Encoding.UTF8.GetString(span));
            }

            public HttpResponseMessage ToHttpResponseMessage(HttpRequestMessage requestMessage)
            {
                var response = new HttpResponseMessage(StatusCode);
                response.RequestMessage = requestMessage;
                Headers.CopyTo(response);

                // TODO check content encoding
                // TODO pipe the content into the response content stream

                return response;
            }
        }

        // TODO create a separate AndroidHttpNative library or something like that

        [LibraryImport(Interop.Libraries.AndroidHttpNative, EntryPoint = "AndroidCryptoNative_HttpMakeRequest")]
        private static unsafe partial PAL_HttpMakeRequestResult MakeRequest(
            Request request,
            Configuration configuration,
            out Response response);

        [LibraryImport(Interop.Libraries.AndroidHttpNative, EntryPoint = "AndroidCryptoNative_HttpFreeResponse")]
        private static unsafe partial void FreeResponse(Response* response);

        public static unsafe HttpResponseMessage MakeRequest(
            HttpRequestMessage requestMessage,
            AndroidMessageHandler handler,
            CancellationToken cancellationToken)
        {
            PAL_HttpMakeRequestResult result;
            Response response;

            if (requestMessage.RequestUri is null)
            {
                // TODO
                throw new InvalidOperationException("Request URI must not be null");
            }

            Uri? proxy = handler.Proxy?.GetProxy(requestMessage.RequestUri!);

            fixed (byte* method = Encoding.UTF8.GetBytes(requestMessage.Method.ToString()))
            fixed (byte* uri = Encoding.UTF8.GetBytes(EncodeUri(requestMessage.RequestUri)))
            fixed (byte* proxyHost = proxy?.Host is not null ? Encoding.UTF8.GetBytes(proxy.Host) : null)
            {
                using var request = new Request
                {
                    Method = method,
                    Uri = uri,
                    Headers = new Headers(CollectHeaders(requestMessage, handler)),
                };

                using var configuration = new Configuration(cancellationToken)
                {
                    Proxy = new Proxy
                    {
                        UseProxy = handler.UseProxy,
                        Host = proxyHost,
                        Port = proxy?.Port ?? -1,
                    },
                    ConnectTimeoutMilliseconds = checked((int)handler.ConnectTimeout.TotalMilliseconds),
                    ReadTimeoutMilliseconds = checked((int)handler.ReadTimeout.TotalMilliseconds),
                    Hooks = new Hooks(handler),
                };

                result = MakeRequest(request, configuration, out response);
            }

            try
            {
                switch (result)
                {
                    case PAL_HttpMakeRequestResult.Success:
                    {
                        // TODO: make sure that this was ported correctly, it doesn't seem right
                        // it's because we're using this URI to store cookies from the response
                        // and the connection URI might be different from the request URI
                        // if it was redirected... but we disabled automatic redirections??
                        requestMessage.RequestUri = response.DecodeConnectionUri();
                        return response.ToHttpResponseMessage(requestMessage);
                    }

                    case PAL_HttpMakeRequestResult.Error:
                        // TODO can we make the error codes more specific?
                        // I guess we need to return more info than an int?
                        throw new HttpRequestException();

                    case PAL_HttpMakeRequestResult.Timeout:
                        // TODO better exception message
                        throw new HttpRequestException("Timeout");

                    case PAL_HttpMakeRequestResult.Cancelled:
                        // the "Cancelled" result is returned only when the cancellation token is cancelled
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new UnreachableException();

                    default:
                        throw new UnreachableException($"Unknown result: {result}");
                }
            }
            finally
            {
                FreeResponse(&response);
            }
        }

        private static string EncodeUri(Uri? uri)
        {
            // TODO can we avoid this throw?
            ArgumentNullException.ThrowIfNull(uri);

            var uriBuilder = new UriBuilder(uri);
            if (uri.IsDefaultPort)
            {
                uriBuilder.Port = -1;
            }

            return uriBuilder.ToString();
        }

        private static Dictionary<string, string> CollectHeaders(HttpRequestMessage request, AndroidMessageHandler handler)
        {
            var accumulatedHeaders = new Dictionary<string, string>();

            if (request.Content is HttpContent content)
                AddHeaders(accumulatedHeaders, content.Headers);

            AddHeaders(accumulatedHeaders, request.Headers);

            // TODO Accept-Encoding headers

            if (handler.UseCookies && handler.CookieContainer != null)
            {
                string cookieHeaderValue = handler.CookieContainer.GetCookieHeader(request.RequestUri!);
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
    }
}
