#include "pal_trust_manager.h"
#include <stdatomic.h>

static _Atomic RemoteCertificateValidationCallback verifyRemoteCertificate;

ARGS_NON_NULL_ALL void AndroidCryptoNative_RegisterRemoteCertificateValidationCallback(RemoteCertificateValidationCallback callback)
{
    atomic_store(&verifyRemoteCertificate, callback);
}

ARGS_NON_NULL_ALL jobjectArray GetTrustManagers(JNIEnv* env, intptr_t sslStreamProxyHandle, char* hostname)
{
    // TrustManagerFactory trustManagerFactory = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm());
    // trustManagerFactory.init((KeyStore)null);
    // TrustManager[] platformTrustManagers = trustManagerFactory.getTrustManagers();
    // X509TrustManager dotnetProxyTrustManager = new DotnetProxyTrustManager(sslStreamProxyHandle, host, platformTrustManagers);
    // TrustManager[] trustManagers = new TrustManager[] { dotnetProxyTrustManager };
    // return trustManagers;

    jobjectArray trustManagers = NULL;
    INIT_LOCALS(loc, dotnetProxyTrustManager, trustManagerFactory, trustManagerFactoryDefaultAlgorithm, platformTrustManagers, javaHostname);

    loc[trustManagerFactoryDefaultAlgorithm] = (*env)->CallStaticObjectMethod(env, g_TrustManagerFactory, g_TrustManagerFactoryGetDefaultAlgorithm);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    loc[trustManagerFactory] = (*env)->CallStaticObjectMethod(env, g_TrustManagerFactory, g_TrustManagerFactoryGetInstance, loc[trustManagerFactoryDefaultAlgorithm]);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    (*env)->CallVoidMethod(env, loc[trustManagerFactory], g_TrustManagerFactoryInit, NULL);

    loc[platformTrustManagers] = (*env)->CallObjectMethod(env, loc[trustManagerFactory], g_TrustManagerFactoryGetTrustManagers);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    loc[javaHostname] = make_java_string(env, hostname);
    loc[dotnetProxyTrustManager] = (*env)->NewObject(env, g_DotnetProxyTrustManager, g_DotnetProxyTrustManagerCtor, (jlong)sslStreamProxyHandle, loc[javaHostname], loc[platformTrustManagers]);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

    trustManagers = make_java_object_array(env, 1, g_TrustManager, loc[dotnetProxyTrustManager]);
    ON_EXCEPTION_PRINT_AND_GOTO(cleanup);

cleanup:
    RELEASE_LOCALS(loc, env);
    return trustManagers;
}

ARGS_NON_NULL_ALL jboolean Java_net_dot_android_crypto_DotnetProxyTrustManager_verifyRemoteCertificate(
    JNIEnv* env, jobject thisHandle, jlong sslStreamProxyHandle, jboolean isTrustedByPlatformTrustManager)
{
    RemoteCertificateValidationCallback verify = atomic_load(&verifyRemoteCertificate);
    abort_unless(verify, "verifyRemoteCertificate callback has not been registered");
    return verify((intptr_t)sslStreamProxyHandle, isTrustedByPlatformTrustManager ? 1 : 0);
}
