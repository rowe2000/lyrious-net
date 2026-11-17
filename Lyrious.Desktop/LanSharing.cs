using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.UILib;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace Lyrious.Desktop;

/// <summary>
/// Sharing on the local network: a web server on every network this computer is on, that passes
/// each request on to the server this app syncs with. That server serves the web app and the API on
/// one address, so a browser on another device gets the web app here and the web app, which talks
/// to where it came from, reaches the API here too (and the hub: web sockets are passed on as
/// well). This is how a server only this computer can reach, e.g. http://localhost:5180, is used
/// from phones on the same network.
/// </summary>
/// <param name="serverAddress">The server this app syncs with now (it can be changed while running).</param>
internal sealed class LanSharing(Func<string> serverAddress) : ILanSharing
{
    private int port;

    // As a browser would reach the server itself: no system proxy, redirects and cookies passed on as they are.
    private readonly HttpMessageInvoker client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    });

    private readonly SemaphoreSlim gate = new(1, 1);
    private WebApplication? server;

    public bool IsRunning => server != null;

    public event Action? Changed;

    public IReadOnlyList<string> Addresses
    {
        get
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(network => network.OperationalStatus == OperationalStatus.Up
                                      && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                    .Select(address => address.Address)
                    // Not the addresses a computer gives itself when it got none (169.254.x.x).
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork
                                      && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    .Select(address => $"http://{address}:{port}")
                    .Distinct()
                    .ToList();
            }
            catch (NetworkInformationException)
            {
                return [];
            }
        }
    }

    public async Task StartAsync(int port)
    {
        await gate.WaitAsync();
        try
        {
            if (server != null && this.port == port)
            {
                return;
            }

            // Running on another port: moved to this one.
            await StopServerAsync();
            this.port = port;

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));
            builder.Services.AddHttpForwarder();
            var app = builder.Build();
            var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
            app.Run(async context =>
            {
                var destination = serverAddress();
                var error = await forwarder.SendAsync(context, destination, client);
                if (error != ForwarderError.None && !context.Response.HasStarted)
                {
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync(
                        $"Lyrious is shared from another computer, which can't reach its server at {destination} right now. Check that the server is running there.");
                }
            });

            try
            {
                await app.StartAsync();
            }
            catch (Exception ex)
            {
                await app.DisposeAsync();
                throw new InvalidOperationException(
                    ex is System.IO.IOException
                        ? $"Port {port} on this computer is used by something else, so Lyrious can't be shared on the network."
                        : $"Couldn't start sharing on the network. {ex.Message}", ex);
            }

            server = app;
            Changed?.Invoke();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try
        {
            await StopServerAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task StopServerAsync()
    {
        if (server is { } app)
        {
            server = null;
            // Not waiting long for the browsers' open connections (the hub's) to close by themselves.
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await app.StopAsync(patience.Token);
            await app.DisposeAsync();
            Changed?.Invoke();
        }
    }
}
