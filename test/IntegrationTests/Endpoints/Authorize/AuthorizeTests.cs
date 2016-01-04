﻿// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using System.Net;
using IdentityModel.Client;
using IdentityServer4.Tests.Common;
using IdentityServer4.Core;
using System.Collections.Generic;
using IdentityServer4.Core.Models;
using IdentityServer4.Core.Services.InMemory;
using System.Security.Claims;

namespace IdentityServer4.Tests.Endpoints.Authorize
{
    public class AuthorizeTests : AuthorizeEndpointTestBase
    {
        const string Category = "Authorize endpoint";

        public AuthorizeTests()
        {
            Clients.AddRange(new Client[] {
                new Client
                {
                    ClientId = "client1",
                    Flow = Flows.Implicit,
                    RequireConsent = false,
                    AllowedScopes = new List<string> { "openid", "profile" },
                    RedirectUris = new List<string> { "https://client1/callback" }
                },
                new Client
                {
                    ClientId = "client2",
                    Flow = Flows.Implicit,
                    RequireConsent = true,
                    AllowedScopes = new List<string> { "openid", "profile", "api1", "api2" },
                    RedirectUris = new List<string> { "https://client2/callback" }
                }
            });

            Users.Add(new InMemoryUser
            {
                Subject = "bob",
                Username = "bob",
                Claims = new Claim[]
                {
                    new Claim("name", "Bob Loblaw"),
                    new Claim("email", "bob@loblaw.com"),
                    new Claim("role", "Attorney"),
                }
            });

            Scopes.AddRange(new Scope[] {
                StandardScopes.OpenId,
                StandardScopes.Profile,
                StandardScopes.Email,
                new Scope
                {
                    Name = "api1",
                    Type = ScopeType.Resource
                },
                new Scope
                {
                    Name = "api2",
                    Type = ScopeType.Resource
                }
            });
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task get_request_should_not_return_404()
        {
            var response = await _client.GetAsync(AuthorizeEndpoint);

            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task get_request_should_not_return_500()
        {
            var response = await _client.GetAsync(AuthorizeEndpoint);

            ((int)response.StatusCode).Should().BeLessThan(500);
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task anonymous_user_should_be_redirected_to_login_page()
        {
            var url = _authorizeRequest.CreateAuthorizeUrl(
                clientId: "client1",
                responseType: "id_token",
                scope: "openid",
                redirectUri: "https://client1/callback",
                state: "123_state",
                nonce: "123_nonce");
            var response = await _client.GetAsync(url);

            _mockPipeline.LoginWasCalled.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task signin_request_should_have_authorization_params()
        {
            var url = _authorizeRequest.CreateAuthorizeUrl(
                clientId: "client1",
                responseType: "id_token",
                scope: "openid",
                redirectUri: "https://client1/callback",
                state: "123_state",
                nonce: "123_nonce",
                loginHint:"login_hint_value",
                acrValues:"acr_1 acr_2 tenant:tenant_value idp:idp_value",
                extra: new {
                    display = "popup", // must use a valid value form the spec for display
                    ui_locales ="ui_locale_value",
                    custom_foo ="foo_value"
                });
            var response = await _client.GetAsync(url);

            _mockPipeline.SignInRequest.Should().NotBeNull();
            _mockPipeline.SignInRequest.ClientId.Should().Be("client1");
            _mockPipeline.SignInRequest.DisplayMode.Should().Be("popup");
            _mockPipeline.SignInRequest.UiLocales.Should().Be("ui_locale_value");
            _mockPipeline.SignInRequest.IdP.Should().Be("idp_value");
            _mockPipeline.SignInRequest.Tenant.Should().Be("tenant_value");
            _mockPipeline.SignInRequest.LoginHint.Should().Be("login_hint_value");
            _mockPipeline.SignInRequest.AcrValues.ShouldAllBeEquivalentTo(new string[] { "acr_2", "acr_1" });
            // todo: add custom params to signin message
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task signin_response_should_allow_successful_authorization_response()
        {
            _mockPipeline.Subject = IdentityServerPrincipal.Create("bob", "Bob Loblaw");
            _mockPipeline.SignInResponse = new SignInResponse();
            _browser.StopRedirectingAfter = 2;

            var url = CreateAuthorizeUrl(
                clientId: "client1",
                responseType: "id_token",
                scope: "openid",
                redirectUri: "https://client1/callback",
                state: "123_state",
                nonce: "123_nonce");
            var response = await _client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location.ToString().Should().StartWith("https://client1/callback");

            var authorization = new IdentityModel.Client.AuthorizeResponse(response.Headers.Location.ToString());
            authorization.IsError.Should().BeFalse();
            authorization.IdentityToken.Should().NotBeNull();
            authorization.State.Should().Be("123_state");
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task authenticated_user_with_valid_request_should_receive_authorization_response()
        {
            await LoginAsync("bob");

            _browser.AllowAutoRedirect = false;

            var url = CreateAuthorizeUrl(
                clientId: "client1",
                responseType: "id_token",
                scope: "openid",
                redirectUri: "https://client1/callback",
                state: "123_state",
                nonce: "123_nonce");
            var response = await _client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location.ToString().Should().StartWith("https://client1/callback");

            var authorization = new IdentityModel.Client.AuthorizeResponse(response.Headers.Location.ToString());
            authorization.IsError.Should().BeFalse();
            authorization.IdentityToken.Should().NotBeNull();
            authorization.State.Should().Be("123_state");
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task client_requires_consent_should_show_consent_page()
        {
            await LoginAsync("bob");

            var url = _authorizeRequest.CreateAuthorizeUrl(
                clientId: "client2",
                responseType: "id_token",
                scope: "openid",
                redirectUri: "https://client2/callback",
                state: "123_state",
                nonce: "123_nonce"
            );
            var response = await _client.GetAsync(url);

            _mockPipeline.ConsentWasCalled.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task consent_page_should_have_authorization_params()
        {
            await LoginAsync("bob");

            var url = _authorizeRequest.CreateAuthorizeUrl(
                clientId: "client2",
                responseType: "id_token token",
                scope: "openid api1 api2",
                redirectUri: "https://client2/callback",
                state: "123_state",
                nonce: "123_nonce",
                acrValues: "acr_1 acr_2 tenant:tenant_value",
                extra: new
                {
                    display = "popup", // must use a valid value form the spec for display
                    ui_locales = "ui_locale_value",
                    custom_foo = "foo_value"
                }
            );
            var response = await _client.GetAsync(url);

            _mockPipeline.ConsentRequest.Should().NotBeNull();
            _mockPipeline.ConsentRequest.ClientId.Should().Be("client2");
            _mockPipeline.ConsentRequest.DisplayMode.Should().Be("popup");
            _mockPipeline.ConsentRequest.UiLocales.Should().Be("ui_locale_value");
            _mockPipeline.ConsentRequest.ScopesRequested.ShouldAllBeEquivalentTo(new string[] { "api2", "openid", "api1" });
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task consent_response_should_allow_successful_authorization_response()
        {
            await LoginAsync("bob");

            _mockPipeline.ConsentResponse = new ConsentResponse()
            {
                ScopesConsented = new string[] { "openid", "api2" }
            };
            _browser.StopRedirectingAfter = 2;

            var url = CreateAuthorizeUrl(
                clientId: "client2",
                responseType: "id_token token",
                scope: "openid profile api1 api2",
                redirectUri: "https://client2/callback",
                state: "123_state",
                nonce: "123_nonce");
            var response = await _client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location.ToString().Should().StartWith("https://client2/callback");

            var authorization = new IdentityModel.Client.AuthorizeResponse(response.Headers.Location.ToString());
            authorization.IsError.Should().BeFalse();
            authorization.IdentityToken.Should().NotBeNull();
            authorization.State.Should().Be("123_state");
            var scopes = authorization.Scope.Split(' ');
            scopes.ShouldAllBeEquivalentTo(new string[] { "api2", "openid" });
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task login_response_and_consent_response_should_receive_authorization_response()
        {
            _mockPipeline.Subject = IdentityServerPrincipal.Create("bob", "Bob Loblaw");
            _mockPipeline.SignInResponse = new SignInResponse();

            _mockPipeline.ConsentResponse = new ConsentResponse()
            {
                ScopesConsented = new string[] { "openid", "api1", "profile" }
            };

            _browser.StopRedirectingAfter = 4;

            var url = CreateAuthorizeUrl(
                clientId: "client2",
                responseType: "id_token token",
                scope: "openid profile api1 api2",
                redirectUri: "https://client2/callback",
                state: "123_state",
                nonce: "123_nonce");
            var response = await _client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location.ToString().Should().StartWith("https://client2/callback");

            var authorization = new IdentityModel.Client.AuthorizeResponse(response.Headers.Location.ToString());
            authorization.IsError.Should().BeFalse();
            authorization.IdentityToken.Should().NotBeNull();
            authorization.State.Should().Be("123_state");
            var scopes = authorization.Scope.Split(' ');
            scopes.ShouldAllBeEquivalentTo(new string[] { "profile", "api1", "openid" });
        }
    }
}
