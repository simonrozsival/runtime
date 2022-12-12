// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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

        // TODO create a separate AndroidHttpNative library or something like that

        [LibraryImport(Interop.Libraries.AndroidHttpNative, EntryPoint = "AndroidCryptoNative_HttpMakeRequest")]
        private static unsafe partial PAL_HttpMakeRequestResult MakeRequest(
            Request request,
            Configuration configuration,
            out Response response);

        [LibraryImport(Interop.Libraries.AndroidHttpNative, EntryPoint = "AndroidCryptoNative_HttpFreeResponse")]
        private static unsafe partial void FreeResponse(ref Response response);

        [LibraryImport(Interop.Libraries.AndroidHttpNative, EntryPoint = "AndroidCryptoNative_HttpCloseInputStream")]
        private static unsafe partial PAL_HttpMakeRequestResult CloseInputStream(IntPtr inputStream);

        [LibraryImport(Interop.Libraries.AndroidHttpNative, EntryPoint = "AndroidCryptoNative_HttpReleaseInputStream")]
        private static unsafe partial void ReleaseInputStream(IntPtr inputStream);

        [LibraryImport(Interop.Libraries.AndroidHttpNative, EntryPoint = "AndroidCryptoNative_HttpReadFromInputStream")]
        private static unsafe partial PAL_HttpMakeRequestResult ReadFromInputStream(IntPtr inputStream, byte* buffer, nuint bufferLength, ref nuint bytesRead);

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
            private const string BrotliEncoding = "br";
            private const string GzipEncoding = "gzip";
            private const string DeflateEncoding = "deflate";

            public HttpStatusCode StatusCode;
            public byte* ConnectionUri;
            public nuint ConnectionUriLength;
            public Headers Headers;
            public IntPtr InputStream;

            public Uri DecodeConnectionUri()
            {
                var span = new ReadOnlySpan<byte>(ConnectionUri, (int)ConnectionUriLength);
                return new Uri(Encoding.UTF8.GetString(span));
            }

            public HttpResponseMessage ToHttpResponseMessage()
            {
                var response = new HttpResponseMessage(StatusCode);

                Headers.CopyTo(response);

                var contentHeaders = response.Content.Headers;
                response.Content = new StreamContent(CreateContentStream(contentHeaders.ContentEncoding));
                response.Content.Headers.AddHeaders(contentHeaders);

                return response;
            }

            private Stream CreateContentStream(ICollection<string> encodings)
            {
                var inputStream = InputStream == IntPtr.Zero
                    ? Stream.Null
                    : new BufferedStream(new ContentStream(InputStream));

                // TODO there's extra code in Xamarin.Android that determines whether or not
                // we need to decompress the data ourselves so I need to look into that later as well

                if (encodings.Contains(BrotliEncoding))
                    return inputStream;
                //     return new BrotliStream(inputStream, CompressionMode.Decompress);
                if (encodings.Contains(GzipEncoding))
                    return inputStream;
                //     return new GZipStream(inputStream, CompressionMode.Decompress);
                else if (encodings.Contains(DeflateEncoding))
                    return inputStream;
                //     return new DeflateStream(inputStream, CompressionMode.Decompress);

                return inputStream;
            }

            private sealed class ContentStream : Stream
            {
                private readonly IntPtr _inputStream;
                public ContentStream(IntPtr inputStream) => _inputStream = inputStream;

                public override bool CanRead => true;
                public override bool CanWrite => false;
                public override bool CanSeek => false;
                public override long Length => throw new NotSupportedException();
                public override long Position
                {
                    get => throw new NotSupportedException();
                    set => throw new NotSupportedException();
                }

                public override void Flush() => throw new NotSupportedException();
                public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                public override void SetLength(long value) => throw new NotSupportedException();

                public override int Read(byte[] buffer, int offset, int count)
                {
                    fixed (byte* bufferPtr = buffer.AsSpan(offset, count))
                    {
                        nuint bytesRead = 0;
                        PAL_HttpMakeRequestResult result = ReadFromInputStream(_inputStream, bufferPtr, (nuint)count, ref bytesRead);

                        // TODO
                        switch (result)
                        {
                            case PAL_HttpMakeRequestResult.Success:
                                return (int)bytesRead;

                            case PAL_HttpMakeRequestResult.Error:
                                throw new IOException("Error reading from input stream");

                            case PAL_HttpMakeRequestResult.Timeout:
                                throw new IOException("Timeout reading from input stream");

                            default:
                                throw new IOException($"Error reading from input stream: {result}");
                        }
                    }
                }

                public override void Write(byte[] buffer, int offset, int count)
                    => throw new InvalidOperationException("Write not supported");

                public override void Close()
                {
                    CloseInputStream(_inputStream);
                }

                protected override void Dispose(bool disposing)
                {
                    ReleaseInputStream(_inputStream);
                }
            }
        }

        public static unsafe HttpResponseMessage MakeRequest(
            Uri requestUri,
            HttpMethod httpMethod,
            Dictionary<string, string> requestHeaders,
            AndroidMessageHandler handler,
            CancellationToken cancellationToken)
        {
            PAL_HttpMakeRequestResult result;
            Response response;

            Uri? proxy = handler.Proxy?.GetProxy(requestUri);

            fixed (byte* method = Encoding.UTF8.GetBytes(httpMethod.ToString()))
            fixed (byte* uri = Encoding.UTF8.GetBytes(EncodeUri(requestUri)))
            fixed (byte* proxyHost = proxy?.Host is not null ? Encoding.UTF8.GetBytes(proxy.Host) : null)
            {
                using var request = new Request
                {
                    Method = method,
                    Uri = uri,
                    Headers = new Headers(requestHeaders),
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
                        return response.ToHttpResponseMessage();

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
                FreeResponse(ref response);
            }
        }

        private static string EncodeUri(Uri uri)
        {
            var uriBuilder = new UriBuilder(uri);
            if (uri.IsDefaultPort)
            {
                uriBuilder.Port = -1;
            }

            return uriBuilder.ToString();
        }
    }
}
