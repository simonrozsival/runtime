// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Net.Http;

internal static class AndroidMessageHandlerFactory
{
    private static bool? s_customHttpMessageHandler;
    private static Type? s_httpMessageHandlerType;
    private static object s_lock = new();
    private const string customHttpMessageHandlerTypeEnvironmentVariable = "XA_HTTP_CLIENT_HANDLER_TYPE";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpMessageHandler CreateNativeHandler()
    {
        lock (s_lock)
        {
            if (!s_customHttpMessageHandler.HasValue)
                s_customHttpMessageHandler = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(customHttpMessageHandlerTypeEnvironmentVariable));

            return s_customHttpMessageHandler.Value
                ? CreateCustomNativeHandler()
                : new AndroidMessageHandler();
        }
    }

    private static HttpMessageHandler CreateCustomNativeHandler()
    {
        if (s_httpMessageHandlerType is null)
        {
            string? handlerTypeName = Environment.GetEnvironmentVariable(customHttpMessageHandlerTypeEnvironmentVariable)?.Trim();
            Type? handlerType = null;
            if (!string.IsNullOrEmpty(handlerTypeName))
            {
                // TODO find the type ... I can't disable IL2057 just with the pragma
// #pragma warning disable IL2057
//                 handlerType = Type.GetType(handlerTypeName, throwOnError: false);
// #pragma warning restore IL2057
            }

            if (handlerType is null || !IsAcceptableHttpMessageHandlerType(handlerType))
            {
                handlerType = typeof(AndroidMessageHandler);
                s_customHttpMessageHandler = false;
            }

            s_httpMessageHandlerType = handlerType;
        }

        // TODO create the type ... I can't disable IL2077 just with the pragma
// #pragma warning disable IL2077
//         return Activator.CreateInstance(s_httpMessageHandlerType) as HttpMessageHandler
//             ?? throw new InvalidOperationException ($"Could not create an instance of HTTP message handler type {s_httpMessageHandlerType.AssemblyQualifiedName}");
// #pragma warning restore IL2077

        throw new NotImplementedException();
    }

    private static bool IsAcceptableHttpMessageHandlerType(Type handlerType)
    {
        // The handler type cannot extend HttpClientHandler to avoid infinite recursion
        if (handlerType.IsAssignableFrom(typeof(HttpClientHandler)))
        {
            // TODO logging
            return false;
        }

        if (!handlerType.IsAssignableFrom(typeof(HttpMessageHandler)))
        {
            // TODO logging
            // Logger.Log(LogLevel.Warn, "MonoAndroid", $"The type {handlerType.AssemblyQualifiedName} set as the default HTTP handler is invalid. Use a type that extends System.Net.Http.HttpMessageHandler.");
            return false;
        }

        return true;
    }
}
