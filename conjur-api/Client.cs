﻿// <copyright file="Client.cs" company="Conjur Inc.">
//     Copyright (c) 2016 Conjur Inc. All rights reserved.
// </copyright>
// <summary>
//     Base Conjur client class implementation.
// </summary>

namespace Conjur
{
    using System;
    using System.Net;
    using System.Net.Security;
    using System.Runtime.Serialization;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Conjur API client.
    /// </summary>
    public class Client
    {
        private ServicePoint sp;
        private Uri applianceUri;
        private string account;

        /// <summary>
        /// Initializes a new instance of the <see cref="Conjur.Client"/> class.
        /// </summary>
        /// <param name="applianceUri">Appliance URI.</param>
        public Client(string applianceUri)
        {
            this.applianceUri = NormalizeBaseUri(applianceUri);
            this.TrustedCertificates = new X509Certificate2Collection();
        }

        /// <summary>
        /// Gets the appliance URI.
        /// </summary>
        /// <value>The appliance URI.</value>
        public Uri ApplianceUri
        {
            get
            { 
                return this.applianceUri;
            }
        }

        /// <summary>
        /// Gets or sets the authenticator used to establish Conjur identity.
        /// This gets automatically set by setting <see cref="Client.Credential"/>.
        /// </summary>
        /// <value>The authenticator.</value>
        public IAuthenticator Authenticator
        {
            get;
            set;
        }

        /// <summary>
        /// Sets the username and API key to authenticate.
        /// This initializes <see cref="Client.Authenticator"/>.
        /// Use <see cref="Client.LogIn"/> to use a password.
        /// </summary>
        /// <value>The credential of user name and API key, where user name is
        /// for example "bob" or "host/jenkins".</value>
        public NetworkCredential Credential
        {
            set
            {
                this.Authenticator = new ApiKeyAuthenticator(
                    new Uri(this.ValidateBaseUri() + "authn"), 
                    value);
            }
        }

        /// <summary>
        /// Gets the collection of extra certificates trusted for authenticating the
        /// Conjur server in addition to the system ones.
        /// </summary>
        /// <value>The trusted certificates collection.</value>
        public X509Certificate2Collection TrustedCertificates
        {
            get;
        }

        /// <summary>
        /// Gets the name of the Conjur organization account.
        /// </summary>
        /// <returns>The account name.</returns>
        public string GetAccountName()
        {
            if (this.account == null)
                this.account = this.Info().account;
            return this.account;
        }

        /// <summary>
        /// Logs in using a password. Sets <see cref="Authenticator"/>
        /// <seealso cref="Credential"/>
        /// </summary>
        /// <returns>The API key.</returns>
        /// <param name="userName">User name to log in as (for example "bob"
        /// or "host/example.com".</param>
        /// <param name="password">Password of the user.</param>
        public string LogIn(string userName, string password)
        {
            return this.LogIn(new NetworkCredential(userName, password));
        }

        /// <summary>
        /// Logs in using a password. Sets <see cref="Authenticator"/>
        /// <seealso cref="Credential"/>
        /// </summary>
        /// <returns>The API key.</returns>
        /// <param name="credential">The credential of user name and password, 
        /// where user name is for example "bob" or "host/jenkins".</param>
        public string LogIn(NetworkCredential credential)
        {
            this.ValidateBaseUri();
            var wr = this.Request("authn/users/login");
            wr.PreAuthenticate = true;
            wr.Credentials = credential;
            var apiKey = wr.Read();

            this.Credential = new NetworkCredential(credential.UserName, apiKey);
            return apiKey;
        }

        /// <summary>
        /// Create a WebRequest for the specified path.
        /// </summary>
        /// <param name="path">Path, NOT including the leading slash.</param>
        /// <returns>A WebRequest for the specified appliance path.</returns>
        public WebRequest Request(string path)
        {
            return WebRequest.Create(this.ValidateBaseUri() + path);
        }

        /// <summary>
        /// Create an authenticated WebRequest for the specified path.
        /// </summary>
        /// <param name="path">Path, NOT including the leading slash.</param>
        /// <returns>A WebRequest for the specified appliance path, with 
        /// authorization header set using <see cref="Authenticator"/>.</returns>
        public WebRequest AuthenticatedRequest(string path)
        {
            return this.ApplyAuthentication(this.Request(path));
        }

        /// <summary>
        /// Validates the appliance base URI.
        /// Tries to connect to /info; if not successful, try again adding an /api prefix.
        /// Also sets up certificate validation.
        /// </summary>
        /// <returns>The validated base appliance URI.</returns>
        public Uri ValidateBaseUri()
        {
            /*
                HACK: This dance is to make sure the validation callback only applies to our server.
                There's no way to set per-request validation in .NET < 4.5, but it is my
                understanding that ServicePoints (which are per-server) store the validators
                for later usage. Thus we set the default validator, make a request to
                instantiate a ServicePoint, then stash it in an instance variable so
                that it doesn't get garbage collected. Finally we restore the original.
            */

            if (this.sp != null)
            {
                return this.applianceUri;
            }

            var oldCallback = ServicePointManager.ServerCertificateValidationCallback;
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = 
                    new RemoteCertificateValidationCallback(this.ValidateCertificate);

                var wr = WebRequest.Create(this.applianceUri + "info");
                wr.Method = "HEAD";
                try
                {
                    wr.GetResponse();
                }
                catch (WebException)
                {
                    // forgotten /api at the end of the Uri? Try again.
                    this.applianceUri = new Uri(this.applianceUri + "api/");
                    wr = WebRequest.Create(this.applianceUri + "info");
                    wr.Method = "HEAD";
                    wr.GetResponse();
                }

                // so it doesn't get garbage collected
                HttpWebRequest hwr = wr as HttpWebRequest;
                if (hwr != null)
                    this.sp = hwr.ServicePoint;

                return this.applianceUri;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = oldCallback;
            }
        }

        /// <summary>
        /// Normalizes the base URI, removing double slashes and adding a trailing
        /// slash, as necessary.
        /// </summary>
        /// <returns>The normalized base URI.</returns>
        /// <param name="uri">Base appliance URI to normalize.</param>
        private static Uri NormalizeBaseUri(string uri)
        {
            // appliance's nginx doesn't like double slashes,
            // eg. it returns 401 on https://example.org//api/info

            // so normalize to remove double slashes
            var normalizedUri = Regex.Replace(uri, "(?<!:)/+", "/");

            // make sure there is a trailing slash
            return new Uri(Regex.Replace(normalizedUri, "(?<!/)\\z", "/"));
        }

        /// <summary>
        /// Validates the Conjur appliance certificate. 
        /// <see cref="RemoteCertificateValidationCallback"/> 
        /// </summary>
        /// <returns><c>true</c>, if certificate was valid, <c>false</c> otherwise.</returns>
        /// <param name="sender">Sender of the validation request.</param>
        /// <param name="certificate">Certificate to be validated.</param>
        /// <param name="chain">Certificate chain, as resolved by the system.</param>
        /// <param name="sslPolicyErrors">SSL policy errors from the system.</param>
        private bool ValidateCertificate(
            object sender, 
            X509Certificate certificate, 
            X509Chain chain, 
            SslPolicyErrors sslPolicyErrors)
        {
            switch (sslPolicyErrors)
            {
                case SslPolicyErrors.RemoteCertificateChainErrors:
                    return chain.VerifyWithExtraRoots(certificate, this.TrustedCertificates);
                case SslPolicyErrors.None:
                    return true;
                default:
                    return false;
            }
        }

        private WebRequest ApplyAuthentication(WebRequest webRequest)
        {
            if (this.Authenticator == null)
                throw new InvalidOperationException("Authentication required.");

            var token = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes(this.Authenticator.GetToken()));
            webRequest.Headers["Authorization"] = "Token token=\"" + token + "\"";
            return webRequest;
        }

        /// <summary>
        /// Get the server info.
        /// </summary>
        /// <returns>Server information.</returns>
        private ServerInfo Info()
        {
            return JsonSerializer<ServerInfo>.Read(this.Request("info"));
        }

        [DataContract]
        internal class ServerInfo
        {
            [DataMember]
            internal string account;
        }
    }
}
