// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include "pal_jni.h"

typedef bool (*IsCancellationRequestedFn)(intptr_t);
typedef void* (*ConfigurationFn)(intptr_t, void*);

struct StringsList
{
    char** Values;
    size_t* Lengths;
};

struct Headers
{
    size_t Count;
    struct StringsList Names;
    struct StringsList Values;
};

struct Request
{
    char* Method;
    char* Uri;
    struct Headers Headers;
    // TODO body
};

struct Response
{
    int32_t StatusCode;
    char* ConnectionUri;
    size_t ConnectionUriLength;
    struct Headers Headers;
    void* InputStream;
};

struct Proxy
{
    bool UseProxy;
    char* Host;
    int32_t Port;
};

struct Hooks
{
    intptr_t AndroidMessageHandler;
    ConfigurationFn ConfigureKeyStore;
    ConfigurationFn ConfigureTrustManagerFactory;
    ConfigurationFn ConfigureKeyManagerFactory;
    ConfigurationFn ConfigureCustomSSLSocketFactory;
};

struct Configuration
{
    struct Proxy Proxy;
    int32_t ConnectTimeoutMilliseconds;
    int32_t ReadTimeoutMilliseconds;
    intptr_t CancellationToken;
    IsCancellationRequestedFn IsCancellationRequested;
    struct Hooks Hooks;
};

// matches PAL_HttpMakeRequestResult
enum
{
    PAL_HttpMakeRequestResult_OK = 0,
    PAL_HttpMakeRequestResult_Error = 1,
    PAL_HttpMakeRequestResult_Timeout = 2,
    PAL_HttpMakeRequestResult_Cancelled = 3,
};
typedef int32_t PAL_HttpMakeRequestResult;

PALEXPORT PAL_HttpMakeRequestResult AndroidCryptoNative_HttpMakeRequest(
    struct Request request,
    struct Configuration configuration,
    struct Response* response);

PALEXPORT void AndroidCryptoNative_HttpFreeResponse(struct Response* response);

PALEXPORT PAL_HttpMakeRequestResult AndroidCryptoNative_HttpReadFromInputStream(void* inputStream, uint8_t* buffer, int32_t length, int32_t* bytesRead);

PALEXPORT PAL_HttpMakeRequestResult AndroidCryptoNative_HttpCloseInputStream(void* inputStream);

PALEXPORT void AndroidCryptoNative_HttpReleaseInputStream(void* inputStream);
