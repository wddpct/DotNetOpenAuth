using System;
using System.Collections.Specialized;
using System.Web.SessionState;
using DotNetOpenId;
using System.Web;
using System.Collections.Generic;
using DotNetOpenId.Provider;
using System.Globalization;

namespace DotNetOpenId.RelyingParty {
	/// <summary>
	/// Provides the programmatic facilities to act as an OpenId consumer.
	/// </summary>
	public class OpenIdRelyingParty {
		IRelyingPartyApplicationStore store;
		Uri request;
		IDictionary<string, string> query;
		
		/// <summary>
		/// Constructs an OpenId consumer that uses the current HttpContext request
		/// and uses the HttpApplication dictionary as its association store.
		/// </summary>
		/// <remarks>
		/// This method requires a current ASP.NET HttpContext.
		/// </remarks>
		public OpenIdRelyingParty() : this(httpApplicationStore, Util.GetRequestUrlFromContext()) { }
		/// <summary>
		/// Constructs an OpenId consumer that uses a given querystring and IAssociationStore.
		/// </summary>
		/// <param name="store">
		/// The application-level store where associations with other OpenId providers can be
		/// preserved for optimized authentication and information about nonces can be stored.
		/// In a multi-server web farm environment, this store MUST be shared across
		/// all servers.
		/// </param>
		/// <param name="requestUrl">
		/// Optional.  The current incoming HTTP request that may contain an OpenId assertion.
		/// If not included, any OpenId authentication assertions will not be processed.
		/// </param>
		/// <remarks>
		/// The IRelyingPartyApplicationStore must be shared across an entire web farm 
		/// because of the design of how nonces are stored/retrieved.  Even if
		/// a given visitor is guaranteed to have affinity toward one server,
		/// replay attacks from another host may be directed at another server,
		/// which must therefore share the nonce information in the application
		/// state store in order to stop the intruder.
		/// </remarks>
		public OpenIdRelyingParty(IRelyingPartyApplicationStore store, Uri requestUrl) {
			if (store == null) throw new NotSupportedException("Stateless mode is not yet supported.");
			this.store = store;
			if (store != null) {
				store.ClearExpiredAssociations(); // every so often we should do this.
			}
			if (requestUrl != null) {
				this.request = requestUrl;
				this.query = Util.NameValueCollectionToDictionary(HttpUtility.ParseQueryString(requestUrl.Query));
			}
		}

		public IAuthenticationRequest CreateRequest(Identifier userSuppliedIdentifier, Realm realm, Uri returnToUrl) {
			return AuthenticationRequest.Create(userSuppliedIdentifier, realm, returnToUrl, store);
		}

		/// <remarks>
		/// This method requires an ASP.NET HttpContext.
		/// </remarks>
		public IAuthenticationRequest CreateRequest(Identifier userSuppliedIdentifier, Realm realm) {
			if (HttpContext.Current == null) throw new InvalidOperationException(Strings.CurrentHttpContextRequired);
			Protocol protocol = Protocol.Default;

			// Build the return_to URL
			UriBuilder returnTo = new UriBuilder(HttpContext.Current.Request.Url);
			// Trim off any parameters with an "openid." prefix, and a few known others
			// to avoid carrying state from a prior login attempt.
			returnTo.Query = string.Empty;
			var returnToParams = new Dictionary<string, string>(HttpContext.Current.Request.QueryString.Count);
			foreach (string key in HttpContext.Current.Request.QueryString) {
				if (!key.StartsWith(protocol.openid.Prefix, StringComparison.OrdinalIgnoreCase) 
					&& key != Token.TokenKey) {
					returnToParams.Add(key, HttpContext.Current.Request.QueryString[key]);
				}
			}
			UriUtil.AppendQueryArgs(returnTo, returnToParams);

			return CreateRequest(userSuppliedIdentifier, realm, returnTo.Uri);
		}

		/// <remarks>
		/// This method requires an ASP.NET HttpContext.
		/// </remarks>
		public IAuthenticationRequest CreateRequest(Identifier userSuppliedIdentifier) {
			if (HttpContext.Current == null) throw new InvalidOperationException(Strings.CurrentHttpContextRequired);

			// Build the realm URL
			UriBuilder realmUrl = new UriBuilder(HttpContext.Current.Request.Url);
			realmUrl.Path = HttpContext.Current.Request.ApplicationPath;
			realmUrl.Query = null;
			realmUrl.Fragment = null;

			return CreateRequest(userSuppliedIdentifier, new Realm(realmUrl.Uri));
		}

		/// <summary>
		/// Gets whether an OpenId provider's response to a prior authentication challenge
		/// is embedded in this web request.
		/// </summary>
		bool isAuthenticationResponseReady {
			get {
				Protocol protocol = Protocol.Detect(query);
				if (!query.ContainsKey(protocol.openid.mode))
					return false;

				if (HttpContext.Current != null && !HttpContext.Current.Request.RequestType.Equals("GET", StringComparison.Ordinal))
					return false;

				return true;
			}
		}
		AuthenticationResponse response;
		/// <summary>
		/// Gets the result of a user agent's visit to his OpenId provider in an
		/// authentication attempt.  Null if no response is available.
		/// </summary>
		public IAuthenticationResponse Response {
			get {
				if (response == null && isAuthenticationResponseReady) {
					response = AuthenticationResponse.Parse(query, store, request);
				}
				return response;
			}
		}

		const string associationStoreKey = "DotNetOpenId.RelyingParty.RelyingParty.AssociationStore";
		static IRelyingPartyApplicationStore httpApplicationStore {
			get {
				HttpContext context = HttpContext.Current;
				if (context == null)
					throw new InvalidOperationException(Strings.IAssociationStoreRequiredWhenNoHttpContextAvailable);
				var store = (IRelyingPartyApplicationStore)context.Application[associationStoreKey];
				if (store == null) {
					context.Application.Lock();
					try {
						if ((store = (IRelyingPartyApplicationStore)context.Application[associationStoreKey]) == null) {
							context.Application[associationStoreKey] = store = new ConsumerApplicationMemoryStore();
						}
					} finally {
						context.Application.UnLock();
					}
				}
				return store;
			}
		}
	}
}
