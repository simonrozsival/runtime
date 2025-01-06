#include "pal_jni.h"

typedef uint8_t (*RemoteCertificateValidationCallback)(intptr_t, uint8_t);

PALEXPORT void AndroidCryptoNative_RegisterRemoteCertificateValidationCallback(RemoteCertificateValidationCallback callback);

jobjectArray GetTrustManagers(JNIEnv* env, intptr_t sslStreamProxyHandle, jobject keyStore, char* hostname);

JNIEXPORT jboolean JNICALL Java_net_dot_android_crypto_DotnetProxyTrustManager_verifyRemoteCertificate(
    JNIEnv *env, jobject thisHandle, jlong sslStreamProxyHandle, jboolean isTrustedByPlatformTrustManager);
