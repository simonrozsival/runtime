// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

package net.dot.android.crypto;

import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;
import android.net.http.X509TrustManagerExtensions;

/**
 * This class is meant to replace the built-in X509TrustManager.
 * Its sole responsibility is to invoke the C# code in the SslStream
 * class during TLS handshakes to perform the validation of the remote
 * peer's certificate.
 */
public final class DotnetProxyTrustManager implements X509TrustManager {
    private final long sslStreamProxyHandle;
    private final X509TrustManager platformTrustManager;
    private final String host;

    public DotnetProxyTrustManager(long sslStreamProxyHandle, String host,  TrustManager[] platformTrustManagers) {
        this.sslStreamProxyHandle = sslStreamProxyHandle;
        this.host = host;

        X509TrustManager platformTrustManager = null;
        for (TrustManager trustManager : platformTrustManagers) {
            if (trustManager instanceof X509TrustManager) {
                platformTrustManager = (X509TrustManager) trustManager;
                break;
            }
        }

        if (platformTrustManager == null) {
            throw new IllegalArgumentException("No X509TrustManager found in the provided TrustManager array.");
        }

        this.platformTrustManager = platformTrustManager;
    }

    public void checkClientTrusted(X509Certificate[] chain, String authType)
            throws CertificateException {
        boolean isTrustedByPlatformTrustManager = true;
        if (platformTrustManager != null) {
            try {
                platformTrustManager.checkClientTrusted(chain, authType);
            } catch (CertificateException e) {
                isTrustedByPlatformTrustManager = false;
            }
        }

        if (!verifyRemoteCertificate(sslStreamProxyHandle, isTrustedByPlatformTrustManager)) {
            throw new CertificateException();
        }
    }

    public void checkServerTrusted(X509Certificate[] chain, String authType)
            throws CertificateException {
        boolean isTrustedByPlatformTrustManager = true;
        if (platformTrustManager != null) {
            try {
                X509TrustManagerExtensions trustManagerExtensions = new X509TrustManagerExtensions(platformTrustManager);
                trustManagerExtensions.checkServerTrusted(chain, authType, host);
            } catch (CertificateException e) {
                isTrustedByPlatformTrustManager = false;
            }
        }

        if (!verifyRemoteCertificate(sslStreamProxyHandle, isTrustedByPlatformTrustManager)) {
            throw new CertificateException();
        }
    }

    public X509Certificate[] getAcceptedIssuers() {
        return new X509Certificate[0];
    }

    static native boolean verifyRemoteCertificate(long sslStreamProxyHandle, boolean isTrustedByPlatformTrustManager);
}
