﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OpenIddict.Client.AspNetCore;

/// <summary>
/// Contains the methods required to ensure that the OpenIddict client configuration is valid.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class OpenIddictClientAspNetCoreConfiguration : IConfigureOptions<AuthenticationOptions>,
                                                              IConfigureOptions<OpenIddictClientOptions>,
                                                              IPostConfigureOptions<AuthenticationOptions>,
                                                              IPostConfigureOptions<OpenIddictClientAspNetCoreOptions>
{
    private readonly IServiceProvider _provider;

    /// <inheritdoc/>
    [Obsolete("This constructor is no longer supported and will be removed in a future version.", error: true)]
    public OpenIddictClientAspNetCoreConfiguration() => throw new NotSupportedException(SR.GetResourceString(SR.ID0403));

    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictClientAspNetCoreConfiguration"/> class.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    public OpenIddictClientAspNetCoreConfiguration(IServiceProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc/>
    public void Configure(AuthenticationOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        // If a handler was already registered and the type doesn't correspond to the OpenIddict handler, throw an exception.
        if (options.SchemeMap.TryGetValue(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme, out var builder) &&
            builder.HandlerType != typeof(OpenIddictClientAspNetCoreHandler))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0288));
        }

        options.AddScheme<OpenIddictClientAspNetCoreHandler>(
            OpenIddictClientAspNetCoreDefaults.AuthenticationScheme, displayName: null);

        // Resolve the forwarded authentication schemes managed by the OpenIddict ASP.NET Core
        // client host and add an entry for each scheme in the ASP.NET Core authentication options.
        foreach (var scheme in _provider.GetRequiredService<IOptionsMonitor<OpenIddictClientAspNetCoreOptions>>()
            .CurrentValue.ForwardedAuthenticationSchemes)
        {
            if (options.SchemeMap.TryGetValue(scheme.Name, out builder) &&
                builder.HandlerType != typeof(OpenIddictClientAspNetCoreForwarder))
            {
                throw new InvalidOperationException(SR.FormatID0414(scheme.Name));
            }

            options.AddScheme<OpenIddictClientAspNetCoreForwarder>(scheme.Name, scheme.DisplayName);
        }
    }

    /// <inheritdoc/>
    public void PostConfigure(string? name, OpenIddictClientAspNetCoreOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!options.DisableAutomaticAuthenticationSchemeForwarding)
        {
            foreach (var (provider, registrations) in _provider.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>()
                .CurrentValue.Registrations
                .Where(registration => !string.IsNullOrEmpty(registration.ProviderName))
                .GroupBy(registration => registration.ProviderName)
                .Select(group => (ProviderName: group.Key, Registrations: group.ToList())))
            {
                // If an explicit mapping was already added, don't overwrite it.
                if (options.ForwardedAuthenticationSchemes.Exists(scheme =>
                    string.Equals(scheme.Name, provider, StringComparison.Ordinal)))
                {
                    continue;
                }

                // Ensure multiple client registrations don't share the same provider
                // name when automatic authentication scheme forwarding is enabled.
                if (registrations is not [OpenIddictClientRegistration registration])
                {
                    throw new InvalidOperationException(SR.FormatID0415(provider));
                }

                options.ForwardedAuthenticationSchemes.Add(new AuthenticationScheme(
                    name       : registration.ProviderName!,
                    displayName: registration.ProviderDisplayName,
                    handlerType: typeof(OpenIddictClientAspNetCoreForwarder)));
            }
        }
    }

    /// <inheritdoc/>
    public void Configure(OpenIddictClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        // Register the built-in event handlers used by the OpenIddict ASP.NET Core client components.
        options.Handlers.AddRange(OpenIddictClientAspNetCoreHandlers.DefaultHandlers);
    }

    /// <inheritdoc/>
    public void PostConfigure(string? name, AuthenticationOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!TryValidate(options.SchemeMap, options.DefaultAuthenticateScheme) ||
            !TryValidate(options.SchemeMap, options.DefaultScheme) ||
            !TryValidate(options.SchemeMap, options.DefaultSignInScheme) ||
            !TryValidate(options.SchemeMap, options.DefaultSignOutScheme))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0289));
        }

#if SUPPORTS_AUTHENTICATION_HANDLER_SELECTION_FALLBACK
        // Starting in ASP.NET 7.0, the authentication stack integrates a fallback
        // mechanism to select the default scheme to use when no value is set, but
        // only if a single handler has been registered in the authentication options.
        //
        // Unfortunately, this behavior is problematic for OpenIddict as it enforces
        // strict checks to prevent calling certain unsafe authentication operations
        // on invalid endpoints. To opt out this undesirable behavior, a fake entry
        // is dynamically added if one of the default schemes properties is not set
        // and less than 2 handlers were registered in the authentication options.
        if (options.SchemeMap.Count < 2 && string.IsNullOrEmpty(options.DefaultScheme) &&
           (string.IsNullOrEmpty(options.DefaultAuthenticateScheme) ||
            string.IsNullOrEmpty(options.DefaultSignInScheme) ||
            string.IsNullOrEmpty(options.DefaultSignOutScheme)))
        {
            options.AddScheme<IAuthenticationHandler>(Guid.NewGuid().ToString(), displayName: null);
        }
#endif

        static bool TryValidate(IDictionary<string, AuthenticationSchemeBuilder> map, string? scheme)
        {
            // If the scheme was not set or if it cannot be found in the map, return true.
            if (string.IsNullOrEmpty(scheme) || !map.TryGetValue(scheme, out var builder))
            {
                return true;
            }

            return builder.HandlerType != typeof(OpenIddictClientAspNetCoreHandler) &&
                   builder.HandlerType != typeof(OpenIddictClientAspNetCoreForwarder);
        }
    }
}
