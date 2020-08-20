﻿using AOT;
using i5.Toolkit.Core.ServiceCore;
using i5.Toolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

#if ENABLE_WINMD_SUPPORT && UNITY_WSA
using Windows.ApplicationModel.Activation;
#endif

namespace i5.Toolkit.Core.OpenIDConnectClient
{
    /// <summary>
    /// Service that implements the OpenID Connect authentification
    /// </summary>
    public class OpenIDConnectService : IUpdateableService
    {
        /// <summary>
        /// List of scopes that the user must agree to and which give the client access to specific data
        /// </summary>
        public string[] Scopes { get; set; } = new string[] { "openid", "profile", "email" };

        /// <summary>
        /// The access token of the logged in user
        /// Use this token to access data about the user or to access protected Web resources
        /// </summary>
        public string AccessToken { get; private set; }

        /// <summary>
        /// Is true if the user of the application is currently logged in
        /// </summary>
        public bool IsLoggedIn { get => !string.IsNullOrEmpty(AccessToken); }

        /// <summary>
        /// The URI which should be shown to the user after the login
        /// For UWP and Android apps, change the Uri schema to something unique
        /// and also change it in the project settings
        /// This way, the app will be opened again on the redirect.
        /// If this URI is not set, a standard page is shown.
        /// </summary>
        public string RedirectURI { get; set; }

        /// <summary>
        /// The provider that should be used for the OpenID Connect procedure
        /// </summary>
        public IOidcProvider OidcProvider { get; set; }

        /// <summary>
        /// A server listener implementation that listens for the redirect
        /// </summary>
        public IRedirectServerListener ServerListener { get; set; }

        /// <summary>
        /// If set to true, the Update method will be executed every frame
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Event which is raised once the login was successfully completed
        /// </summary>
        public event EventHandler LoginCompleted;
        /// <summary>
        /// Event which is reaised once the logout was completed
        /// </summary>
        public event EventHandler LogoutCompleted;

        /// <summary>
        /// Cached event arguments of the last received redirect
        /// </summary>
        private RedirectReceivedEventArgs eventArgs;

#if ENABLE_WINMD_SUPPORT && UNITY_WSA
        [DllImport("__Internal")]
        extern static void SetupActivatedEventCallback(AppActivatedDelegate callback);

        delegate void AppActivatedDelegate(IActivatedEventArgs activatedArgs);

        [MonoPInvokeCallback(typeof(AppActivatedDelegate))]
        static void OnAppActivated(IActivatedEventArgs activatedArgs)
        {
            if (UnityEngine.WSA.Application.RunningOnAppThread())
            {
                HandleActivation(activatedArgs);
            }
            else
            {
                UnityEngine.WSA.Application.InvokeOnAppThread(() =>
                {
                    HandleActivation(activatedArgs);
                }, false);
            }
        }

        static void HandleActivation(IActivatedEventArgs activatedArgs)
        {
            if (activatedArgs.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = (ProtocolActivatedEventArgs)activatedArgs;
                Dictionary<string, string> fragments = UriUtils.GetUriParameters(protocolArgs.Uri);
                Debug.LogError(protocolArgs.Uri);
                OpenIDConnectService oidcService = ServiceManager.GetService<OpenIDConnectService>();
                oidcService.ServerListener_RedirectReceived(
                    null,
                    new RedirectReceivedEventArgs(fragments, oidcService.RedirectURI));
            }
        }

#endif

        /// <summary>
        /// Creates a new instance of the OpenID Connect service
        /// </summary>
        public OpenIDConnectService()
        {
            ServerListener = new RedirectServerListener();
        }

        /// <summary>
        /// Initialization which is called by the server manager once the service is registered
        /// </summary>
        /// <param name="owner">The service manager that owns this service</param>
        public void Initialize(IServiceManager owner)
        {
#if ENABLE_WINMD_SUPPORT && UNITY_WSA
        SetupActivatedEventCallback(OnAppActivated);
#endif
        }

        /// <summary>
        /// Called by the service manager once the service is unregistered
        /// Stops the server if it is running and logs the user out
        /// </summary>
        public void Cleanup()
        {
            if (ServerListener.ServerActive)
            {
                ServerListener.StopServerImmediately();
            }

            if (IsLoggedIn)
            {
                Logout();
            }
        }

        /// <summary>
        /// Opens a login page in the system's default browser so that the user can log in
        /// Requires a configured OpenID Connect provider
        /// </summary>
        public void OpenLoginPage()
        {
            if (OidcProvider == null)
            {
                i5Debug.LogError("OIDC provider is not set. Please set the OIDC provider before accessing the OIDC workflow.", this);
                return;
            }

#if !ENABLE_WINMD_SUPPORT || !UNITY_WSA

            if (ServerListener == null)
            {
                i5Debug.LogError("Redirect server listener is not set. Please set it before accessing the OIDC workflow.", this);
                return;
            }

            if (ServerListener.ServerActive)
            {
                OidcProvider.OpenLoginPage(Scopes, ServerListener.ListeningUri);
            }
            else
            {
                if (string.IsNullOrEmpty(ServerListener.ListeningUri))
                {
                    ServerListener.GenerateListeningUri();
                }
                string urlStart = "<html><head>";
                string customAdditionalRedirect = "";
                if (!string.IsNullOrEmpty(RedirectURI))
                {
                    customAdditionalRedirect = string.Format("<meta http-equiv=\"Refresh\" content=\"0; url = {0}\" />"
                        , RedirectURI);
                }
                string urlEnd = "</head><body>Please return to the app</body></html>";
                ServerListener.ResponseString = urlStart + customAdditionalRedirect + urlEnd;
                ServerListener.RedirectReceived += ServerListener_RedirectReceived;
                ServerListener.StartServer();

                OidcProvider.OpenLoginPage(Scopes, ServerListener.ListeningUri);
            }
#else
            OidcProvider.OpenLoginPage(Scopes, RedirectURI);
#endif
        }

        /// <summary>
        /// Called by the server listener once a redirect was received
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the redirect event</param>
        public void ServerListener_RedirectReceived(object sender, RedirectReceivedEventArgs e)
        {
            ServerListener.RedirectReceived -= ServerListener_RedirectReceived;
            eventArgs = e;
            // this method is executed by the thread that raised the event - the server's thread
            // enable the processing of the Update method so that we process the redirect on the main thread
            Enabled = true;
        }

        /// <summary>
        /// Logs the user out
        /// </summary>
        public void Logout()
        {
            AccessToken = "";
            LogoutCompleted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Checks if the access token is valid
        /// </summary>
        /// <returns>Returns true if the access token could be verified at the provider</returns>
        public async Task<bool> CheckAccessToken()
        {
            if (!IsLoggedIn)
            {
                i5Debug.LogWarning("Access token not valid because user is not logged in.", this);
                return false;
            }
            IUserInfo userInfo = await GetUserDataAsync();
            return userInfo != null;
        }

        /// <summary>
        /// Gets the user's information
        /// </summary>
        /// <returns>Returns user data from the OIDC provider</returns>
        public async Task<IUserInfo> GetUserDataAsync()
        {
            if (!IsLoggedIn)
            {
                i5Debug.LogError("Please log in first before accessing user data", this);
                return null;
            }
            if (OidcProvider == null)
            {
                i5Debug.LogError("OIDC provider is not set. Please set the OIDC provider before accessing the OIDC workflow.", this);
                return null;
            }
            return await OidcProvider.GetUserInfoAsync(AccessToken);
        }

        /// <summary>
        /// Called each frame by the service manager
        /// Handles the redirect processing on the main thread
        /// </summary>
        public async void Update()
        {
            // if we did not cache a redirect event argument: nothing to do
            if (eventArgs == null)
            {
                return;
            }

            // disable immediately again so that we do not execute this part here multiple times
            // as long as the first operation takes to finish
            Enabled = false;

            if (OidcProvider.ParametersContainError(eventArgs.RedirectParameters, out string errorMessage))
            {
                i5Debug.LogError("Error: " + errorMessage, this);
                return;
            }

            if (OidcProvider.AuthorizationFlow == AuthorizationFlow.AUTHORIZATION_CODE)
            {
                string authorizationCode = OidcProvider.GetAuthorizationCode(eventArgs.RedirectParameters);
                AccessToken = await OidcProvider.GetAccessTokenFromCodeAsync(authorizationCode, eventArgs.RedirectUri);
            }
            else
            {
                AccessToken = OidcProvider.GetAccessToken(eventArgs.RedirectParameters);
            }
            eventArgs = null;
            LoginCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}