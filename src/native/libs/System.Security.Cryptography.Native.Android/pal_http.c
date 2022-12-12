// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "pal_http.h"

#define WHEN_NOT_OK_GOTO(ret, label) \
    if (ret != PAL_HttpMakeRequestResult_OK) \
        goto label

ARGS_NON_NULL_ALL static void FreeStringsList(struct StringsList* list, size_t count)
{
    if (list->Values != NULL)
    {
        for (size_t i = 0; i < count; ++i)
        {
            if (list->Values[i] != NULL)
                free(list->Values[i]);
        }
    }

    if (list->Lengths != NULL)
        free(list->Lengths);
}

ARGS_NON_NULL_ALL static void FreeHeaders(struct Headers* headers)
{
    FreeStringsList(&headers->Names, headers->Count);
    FreeStringsList(&headers->Values, headers->Count);
}

ARGS_NON_NULL_ALL static jobject ConfigureKeyStore(struct Configuration* config, jobject keyStore)
{
    return (jobject)config->Hooks.ConfigureKeyStore(config->Hooks.AndroidMessageHandler, keyStore);
}

ARGS_NON_NULL_ALL static jobject ConfigureTrustManagerFactory(struct Configuration* config, jobject keyStore)
{
    return (jobject)config->Hooks.ConfigureTrustManagerFactory(config->Hooks.AndroidMessageHandler, keyStore);
}

ARGS_NON_NULL_ALL static jobject ConfigureKeyManagerFactory(struct Configuration* config, jobject keyStore)
{
    return (jobject)config->Hooks.ConfigureKeyManagerFactory(config->Hooks.AndroidMessageHandler, keyStore);
}

ARGS_NON_NULL_ALL static jobject ConfigureCustomSSLSocketFactory(struct Configuration* config, jobject httpsUrlConnection)
{
    return (jobject)config->Hooks.ConfigureCustomSSLSocketFactory(config->Hooks.AndroidMessageHandler, httpsUrlConnection);
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult Connect(JNIEnv* env, jobject httpUrlConnection)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, exception);

    (*env)->CallVoidMethod(env, httpUrlConnection, g_HttpURLConnectionConnect);

    loc[exception] = (*env)->ExceptionOccurred(env);
    if (loc[exception] != NULL)
    {
        (*env)->ExceptionDescribe(env);
        (*env)->ExceptionClear(env);

        ret = (*env)->IsInstanceOf(env, loc[exception], g_SocketTimeoutExceptionClass)
            ? PAL_HttpMakeRequestResult_Timeout
            : PAL_HttpMakeRequestResult_Error;

        goto cleanup;
    }

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult Disconnect(JNIEnv* env, jobject httpUrlConnection)
{
    (*env)->CallVoidMethod(env, httpUrlConnection, g_HttpURLConnectionDisconnect);

    return TryClearJNIExceptions(env)
        ? PAL_HttpMakeRequestResult_Error
        : PAL_HttpMakeRequestResult_OK;
}

ARGS_NON_NULL_ALL static bool IsCancellationRequested(struct Configuration* config)
{
    return config->IsCancellationRequested(config->CancellationToken);
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CreateProxy(
    JNIEnv* env, struct Configuration* config, jobject* proxy)
{
    if (!config->Proxy.UseProxy || !config->Proxy.Host)
    {
        *proxy = (*env)->GetStaticObjectField(env, g_ProxyClass, g_ProxyNoProxy);
        return TryClearJNIExceptions(env)
            ? PAL_HttpMakeRequestResult_Error // TODO what error should we return here?
            : PAL_HttpMakeRequestResult_OK;
    }

    INIT_LOCALS(loc, address, proxyType);
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;

    loc[address] = (*env)->NewObject(env, g_InetSocketAddressClass, g_InetSocketAddressCtor, config->Proxy.Host, config->Proxy.Port);

    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    // creating InetSocketAddress can take a long time, so check for cancellation after we return from it
    if (IsCancellationRequested(config))
    {
        ret = PAL_HttpMakeRequestResult_Cancelled;
        goto cleanup;
    }

    loc[proxyType] = (*env)->GetStaticObjectField(env, g_ProxyTypeEnum, g_ProxyTypeHttp);

    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    *proxy = (*env)->NewObject(env, g_ProxyClass, g_ProxyCtor, loc[proxyType], loc[address]);

    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult OpenUrlConnection(
    JNIEnv* env,
    struct Request* request,
    struct Configuration* config,
    jobject* httpUrlConnection)
{
    INIT_LOCALS(loc, proxy, urlString, url);
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;

    ret = CreateProxy(env, config, &loc[proxy]);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    loc[urlString] = make_java_string(env, request->Uri);
    loc[url] = (*env)->NewObject(env, g_URLClass, g_URLCtor, loc[urlString]);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    // TODO is this a good spot to check for cancellation?
    if (IsCancellationRequested(config))
    {
        ret = PAL_HttpMakeRequestResult_Cancelled;
        goto cleanup;
    }

    *httpUrlConnection = loc[proxy] == NULL
        ? (*env)->CallObjectMethod(env, loc[url], g_URLOpenConnection)
        : (*env)->CallObjectMethod(env, loc[url], g_URLOpenConnectionWithProxy, loc[proxy]);

    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CreateKeyStore(
    JNIEnv* env, struct Configuration* config, jobject* keyStore)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, tmpKeyStore, keyStoreDefaultType, kmf, kmfType);

    loc[keyStoreDefaultType] = (*env)->CallStaticObjectMethod(env, g_KeyStoreClass, g_KeyStoreGetDefaultType);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    loc[tmpKeyStore] = (*env)->CallStaticObjectMethod(env, g_KeyStoreClass, g_KeyStoreGetInstance, loc[keyStoreDefaultType]);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    (*env)->CallVoidMethod(env, loc[tmpKeyStore], g_KeyStoreLoad, NULL, NULL);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    // TODO load trusted certificates passed through the config

    jobject keyStoreGRef = ToGRef(env, loc[tmpKeyStore]);
    loc[tmpKeyStore] = NULL;

    *keyStore = ConfigureKeyStore(config, keyStoreGRef);

    // only release the global ref if we don't get the same reference back
    if (*keyStore != keyStoreGRef)
        ReleaseGRef(env, keyStoreGRef);

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CreateKeyManagers(
    JNIEnv* env, struct Configuration* config, jobject keyStore, jobject* keyManagers)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;

    jobject keyManagerFactoryGRef = ConfigureKeyManagerFactory(config, keyStore);
    if (keyManagerFactoryGRef == NULL)
    {
        *keyManagers = NULL;
    }
    else
    {
        *keyManagers = (*env)->CallObjectMethod(env, keyManagerFactoryGRef, g_KeyManagerFactoryGetKeyManagers);
        ReleaseGRef(env, keyManagerFactoryGRef);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
    }

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CreateTrustManagers(
    JNIEnv* env, struct Configuration* config, jobject keyStore, jobject* trustManagers)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, trustManagerFactory, algorithm);

    jobject trustManagerFactoryGRef = ConfigureTrustManagerFactory(config, keyStore);

    // If the trust manager factory was not configured through a hook, use the default
    if (trustManagerFactoryGRef == NULL)
    {
        // TODO check if there's a custom validation callback or if we have trusted certs
        // bool needsTrustManager = config->ValidationCallback != NULL && config->TrustedCertificates != NULL;
        bool needsTrustManager = false;

        if (needsTrustManager)
        {
            // TODO load custom trust manager to validate the server certificate

            loc[algorithm] = (*env)->CallStaticObjectMethod(env, g_TrustManagerFactoryClass, g_TrustManagerFactoryGetDefaultAlgorithm);
            ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

            loc[trustManagerFactory] = (*env)->CallStaticObjectMethod(env, g_TrustManagerFactoryClass, g_TrustManagerFactoryGetInstance, loc[algorithm]);
            ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

            (*env)->CallVoidMethod(env, loc[trustManagerFactory], g_TrustManagerFactoryInit, keyStore);
            ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

            *trustManagers = (*env)->CallObjectMethod(env, loc[trustManagerFactory], g_TrustManagerFactoryGetTrustManagers);
            ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
        }
        else
        {
            *trustManagers = NULL;
        }
    }
    else
    {
        *trustManagers = (*env)->NewLocalRef(env, trustManagerFactoryGRef);
        ReleaseGRef(env, trustManagerFactoryGRef);
    }
    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult GetSSLContext(JNIEnv* env, jobject* sslContext)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, protocol, context);

    loc[protocol] = make_java_string(env, "TLSv1.3");
    loc[context] = (*env)->CallStaticObjectMethod(env, g_SSLContext, g_SSLContextGetInstanceMethod, loc[protocol]);

    // If TLSv1.3 is not supported (API 21-28), try TLSv1.2
    if (TryClearJNIExceptions(env))
    {
        ReleaseLRef(env, loc[protocol]);
        loc[protocol] = make_java_string(env, "TLSv1.2");
        loc[context] = (*env)->CallStaticObjectMethod(env, g_SSLContext, g_SSLContextGetInstanceMethod, loc[protocol]);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
    }

    *sslContext = (*env)->NewLocalRef(env, loc[context]);
    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CreateSslSocketFactory(
    JNIEnv* env, struct Configuration* config, jobject* sslSocketFactory)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, keyManagers, trustManagers, context);
    jobject keyStore = NULL;

    ret = CreateKeyStore(env, config, &keyStore);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = CreateKeyManagers(env, config, keyStore, &loc[keyManagers]);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = CreateTrustManagers(env, config, keyStore, &loc[trustManagers]);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = GetSSLContext(env, &loc[context]);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    // context.init(keyManagers, trustManagers, null);
    (*env)->CallVoidMethod(env, loc[context], g_SSLContextInitMethod, loc[keyManagers], loc[trustManagers], NULL);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    *sslSocketFactory = (*env)->CallObjectMethod(env, loc[context], g_SSLContextGetSocketFactoryMethod);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    ReleaseGRef(env, keyStore);
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult SetupSsl(
    JNIEnv* env, struct Configuration* config, jobject httpsUrlConnection)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, sslSocketFactory);

    jobject sslSocketFactoryGRef = ConfigureCustomSSLSocketFactory(config, httpsUrlConnection);
    if (sslSocketFactoryGRef != NULL)
    {
        loc[sslSocketFactory] = (*env)->NewLocalRef(env, sslSocketFactoryGRef);
        ReleaseGRef(env, sslSocketFactoryGRef);
    }
    else
    {
        ret = CreateSslSocketFactory(env, config, &loc[sslSocketFactory]);
        WHEN_NOT_OK_GOTO(ret, cleanup);
    }

    (*env)->CallVoidMethod(env, httpsUrlConnection, g_HttpsURLConnectionSetSSLSocketFactory, loc[sslSocketFactory]);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult SetupHeaders(
    JNIEnv* env, struct Headers* headers, jobject httpUrlConnection)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, name, value);

    for (size_t i = 0; i < (size_t)headers->Count; i++)
    {
        loc[name] = make_java_string(env, headers->Names.Values[i]);
        loc[value] = make_java_string(env, headers->Values.Values[i]);
        (*env)->CallVoidMethod(env, httpUrlConnection, g_HttpURLConnectionAddRequestProperty, loc[name], loc[value]);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

        ReleaseLRef(env, loc[name]);
        ReleaseLRef(env, loc[value]);
    }

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult SetupBody(
    JNIEnv* env, struct Request* request, jobject httpUrlConnection)
{
    // TODO
    return PAL_HttpMakeRequestResult_OK;
}

static PAL_HttpMakeRequestResult SetupRequest(
    JNIEnv* env, struct Request* request, struct Configuration* config, jobject httpUrlConnection)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, method);

    loc[method] = make_java_string(env, request->Method);
    (*env)->CallVoidMethod(env, httpUrlConnection, g_HttpURLConnectionSetRequestMethod, loc[method]);
    // HttpURLConnection doesn't support all methods, so we should return a different error if this call failed
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    if ((*env)->IsInstanceOf(env, httpUrlConnection, g_HttpsURLConnection))
    {
        ret = SetupSsl(env, config, httpUrlConnection);
        WHEN_NOT_OK_GOTO(ret, cleanup);
    }

    // TODO setup hostname verifier (for ServerCertificateValidationCallback)

    ret = SetupHeaders(env, &request->Headers, httpUrlConnection);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = SetupBody(env, request, httpUrlConnection);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    // we handle redirect ourselves in AndroidMessageHandler
    (*env)->SetBooleanField(env, httpUrlConnection, g_HttpURLConnectionInstanceFollowRedirectsField, false);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    if (config->ConnectTimeoutMilliseconds > 0)
    {
        (*env)->CallVoidMethod(env, httpUrlConnection, g_HttpURLConnectionSetConnectTimeout, config->ConnectTimeoutMilliseconds);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
    }

    if (config->ReadTimeoutMilliseconds > 0)
    {
        (*env)->CallVoidMethod(env, httpUrlConnection, g_HttpURLConnectionSetReadTimeout, config->ReadTimeoutMilliseconds);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
    }

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CopyUtf8String(JNIEnv* env, jstring str, char** out, size_t* length)
{
    char* tmp = NULL;

    size_t utf8length = (size_t)(*env)->GetStringUTFLength(env, str);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    const char* utf8 = (*env)->GetStringUTFChars(env, str, NULL);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    tmp = xcalloc(utf8length + 1, sizeof(char));
    memcpy(tmp, utf8, utf8length);
    tmp[utf8length] = '\0';

    (*env)->ReleaseStringUTFChars(env, str, utf8);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    *out = tmp;
    *length = utf8length;

    return PAL_HttpMakeRequestResult_OK;

cleanup:
    if (tmp != NULL)
    {
        free(tmp);
    }

    return PAL_HttpMakeRequestResult_Error;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CountHeaders(
    JNIEnv* env, jobject httpUrlConnection, size_t* headersCount, bool* skipFirstHeader)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, headerName, headerValue);
    *skipFirstHeader = false;

    for (size_t i = 0;; ++i)
    {
        loc[headerName] = (*env)->CallObjectMethod(env, httpUrlConnection, g_HttpURLConnectionGetHeaderFieldKey, (int32_t)i);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

        if (i == 0 && loc[headerName] == NULL)
        {
            // The first header can be NULL.
            // If it is we should skip it.
            *skipFirstHeader = true;
        }
        else if (loc[headerName] == NULL)
        {
            *headersCount = *skipFirstHeader ? i - 1 : i;
            ret = PAL_HttpMakeRequestResult_OK;
            break;
        }

        ReleaseLRef(env, loc[headerName]);
    }

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CollectHeadersUsingMethod(
    JNIEnv* env, jobject httpUrlConnection, jmethodID method, struct StringsList* out, size_t count, bool skipFirstHeader)
{
    INIT_LOCALS(loc, tmp);
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;

    out->Values = xcalloc(count, sizeof(char*));
    out->Lengths = xcalloc(count, sizeof(int32_t));

    size_t offset = skipFirstHeader ? 1 : 0;
    for (size_t i = 0; i < count; ++i)
    {
        loc[tmp] = (*env)->CallObjectMethod(env, httpUrlConnection, method, i + offset);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

        ret = CopyUtf8String(env, loc[tmp], &out->Values[i], &out->Lengths[i]);
        WHEN_NOT_OK_GOTO(ret, cleanup);

        ReleaseLRef(env, loc[tmp]);
        loc[tmp] = NULL;
    }

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CollectHeaderNames(
    JNIEnv* env, jobject httpUrlConnection, struct StringsList* names, size_t count, bool skipFirstHeader)
{
    return CollectHeadersUsingMethod(env, httpUrlConnection, g_HttpURLConnectionGetHeaderFieldKey, names, count, skipFirstHeader);
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult CollectHeaderValues(
    JNIEnv* env, jobject httpUrlConnection, struct StringsList* values, size_t count, bool skipFirstHeader)
{
    return CollectHeadersUsingMethod(env, httpUrlConnection, g_HttpURLConnectionGetHeaderField, values, count, skipFirstHeader);
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult GetHeaders(
    JNIEnv* env, jobject httpUrlConnection, struct Headers* headers)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, headerName, headerValue);
    bool skipFirstHeader = false;

    ret = CountHeaders(env, httpUrlConnection, &headers->Count, &skipFirstHeader);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = CollectHeaderNames(env, httpUrlConnection, &headers->Names, headers->Count, skipFirstHeader);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = CollectHeaderValues(env, httpUrlConnection, &headers->Values, headers->Count, skipFirstHeader);
    WHEN_NOT_OK_GOTO(ret, cleanup);

cleanup:
    if (ret != PAL_HttpMakeRequestResult_OK)
    {
        FreeHeaders(headers);
    }

    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult GetInputStream(
    JNIEnv* env, jobject httpUrlConnection, struct Response* response)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, inputStream, buffer);

    int statusCode = (*env)->CallIntMethod(env, httpUrlConnection, g_HttpURLConnectionGetResponseCode);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    bool isErrorStatusCode = statusCode >= 400 && statusCode <= 599;
    loc[inputStream] = isErrorStatusCode
        ? (*env)->CallObjectMethod(env, httpUrlConnection, g_HttpURLConnectionGetErrorStream)
        : (*env)->CallObjectMethod(env, httpUrlConnection, g_HttpURLConnectionGetInputStream);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    response->InputStream = ToGRef(env, loc[inputStream]);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    loc[inputStream] = NULL;

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

ARGS_NON_NULL_ALL static PAL_HttpMakeRequestResult ReadResponse(
    JNIEnv* env,
    jobject httpUrlConnection,
    struct Configuration* config,
    struct Response* response)
{
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, connectionUri, connectionUriStr);

    response->StatusCode = (*env)->CallIntMethod(env, httpUrlConnection, g_HttpURLConnectionGetResponseCode);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    // Get the connection URI in case of redirection
    loc[connectionUri] = (*env)->CallObjectMethod(env, httpUrlConnection, g_HttpURLConnectionGetURL);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
    loc[connectionUriStr] = (*env)->CallObjectMethod(env, loc[connectionUri], g_URLToString);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
    ret = CopyUtf8String(env, loc[connectionUriStr], &response->ConnectionUri, &response->ConnectionUriLength);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    if (IsCancellationRequested(config))
    {
        ret = PAL_HttpMakeRequestResult_Cancelled;
        goto cleanup;
    }

    ret = GetHeaders(env, httpUrlConnection, &response->Headers);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = GetInputStream(env, httpUrlConnection, response);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

PAL_HttpMakeRequestResult AndroidCryptoNative_HttpMakeRequest(
    struct Request request,
    struct Configuration config,
    struct Response* response)
{
    abort_if_invalid_pointer_argument(response);

    JNIEnv* env = GetJNIEnv();
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;
    INIT_LOCALS(loc, httpUrlConnection);

    ret = OpenUrlConnection(env, &request, &config, &loc[httpUrlConnection]);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = SetupRequest(env, &request, &config, loc[httpUrlConnection]);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = Connect(env, loc[httpUrlConnection]);
    WHEN_NOT_OK_GOTO(ret, cleanup);

    ret = ReadResponse(env, loc[httpUrlConnection], &config, response);
    WHEN_NOT_OK_GOTO(ret, cleanup);

cleanup:
    if (loc[httpUrlConnection] != NULL)
    {
        // Disconnect(env, loc[httpUrlConnection]);
    }

    RELEASE_LOCALS(loc, env);
    return ret;
}


void AndroidCryptoNative_HttpFreeResponse(
    struct Response* response)
{
    // Only free headers
    // The input stream is closed separately
    FreeHeaders(&response->Headers);
}


PAL_HttpMakeRequestResult AndroidCryptoNative_HttpReadFromInputStream(void* inputStream, uint8_t* buffer, int32_t length, int32_t* bytesRead)
{
    abort_if_invalid_pointer_argument(inputStream);
    abort_if_invalid_pointer_argument(buffer);
    abort_if_invalid_pointer_argument(bytesRead);

    JNIEnv* env = GetJNIEnv();
    INIT_LOCALS(loc, tmp);
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;

    // TODO can we keep the buffer to avoid allocating it over and over again?
    loc[tmp] = make_java_byte_array(env, length);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    *bytesRead = (*env)->CallIntMethod(env, (jobject)inputStream, g_InputStreamRead, loc[tmp], 0, length);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    if (*bytesRead == -1)
    {
        *bytesRead = 0;
    }
    else if (*bytesRead > 0)
    {
        (*env)->GetByteArrayRegion(env, loc[tmp], 0, *bytesRead, (jbyte*)buffer);
        ON_EXCEPTION_PRINT_AND_GOTO(cleanup);
    }

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    RELEASE_LOCALS(loc, env);
    return ret;
}

PAL_HttpMakeRequestResult AndroidCryptoNative_HttpCloseInputStream(void* inputStream)
{
    abort_if_invalid_pointer_argument(inputStream);

    JNIEnv* env = GetJNIEnv();
    PAL_HttpMakeRequestResult ret = PAL_HttpMakeRequestResult_Error;

    (*env)->CallVoidMethod(env, (jobject)inputStream, g_InputStreamClose);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    ret = PAL_HttpMakeRequestResult_OK;

cleanup:
    return ret;
}

void AndroidCryptoNative_HttpReleaseInputStream(void* inputStream)
{
    abort_if_invalid_pointer_argument(inputStream);

    JNIEnv* env = GetJNIEnv();
    ReleaseGRef(env, (jobject)inputStream);
}



