using System.Net.WebSockets;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Lyrious.Api.Services
{
	public class MqttService : IHostedService
	{
		private const string ServerClientId = "SERVER";
		private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

		private readonly MqttServer _mqttServer;
		private readonly ILogger<MqttService> _logger;
		private readonly IServiceScopeFactory _scopeFactory;
		private readonly bool _allowAnonymousAccess;

		public MqttService(MqttServer mqttServer, ILogger<MqttService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration)
		{
			_mqttServer = mqttServer;
			_logger = logger;
			_scopeFactory = scopeFactory;
			_allowAnonymousAccess = configuration.GetValue("Mqtt:AllowAnonymousAccess", false);
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			// Set up server event handlers
			_mqttServer.ValidatingConnectionAsync += OnValidatingConnectionAsync;
			_mqttServer.ClientConnectedAsync += OnClientConnectedAsync;
			_mqttServer.ClientDisconnectedAsync += OnClientDisconnectedAsync;
			_mqttServer.InterceptingPublishAsync += OnInterceptingPublishAsync;
			_mqttServer.ApplicationMessageNotConsumedAsync += OnMessageNotConsumedAsync;

			// Start the MQTT server
			await _mqttServer.StartAsync();

			_logger.LogInformation("MQTT server started successfully on port 1883");

			// Set up default subscriptions
			await SetupDefaultTopicsAsync();
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			// Unregister event handlers
			_mqttServer.ValidatingConnectionAsync -= OnValidatingConnectionAsync;
			_mqttServer.ClientConnectedAsync -= OnClientConnectedAsync;
			_mqttServer.ClientDisconnectedAsync -= OnClientDisconnectedAsync;
			_mqttServer.InterceptingPublishAsync -= OnInterceptingPublishAsync;
			_mqttServer.ApplicationMessageNotConsumedAsync -= OnMessageNotConsumedAsync;

			// Stop the MQTT server
			await _mqttServer.StopAsync();

			_logger.LogInformation("MQTT server stopped");
		}

		/// <summary>
		/// Authenticates MQTT clients against ASP.NET Identity. A client that sends a user name must
		/// supply that member's password; anonymous clients are only let in when Mqtt:AllowAnonymousAccess
		/// is true, and can never write data. A non-empty UserName on later events is therefore verified.
		/// </summary>
		private async Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs args)
		{
			if (string.IsNullOrEmpty(args.UserName))
			{
				if (!_allowAnonymousAccess)
				{
					args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
				}

				return;
			}

			using var scope = _scopeFactory.CreateScope();
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
			var member = await userManager.FindByNameAsync(args.UserName);
			if (member == null || !await userManager.CheckPasswordAsync(member, args.Password ?? string.Empty))
			{
				_logger.LogWarning("MQTT client {ClientId} failed authentication as {UserName}", args.ClientId, args.UserName);
				args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
			}
		}

		private Task OnClientConnectedAsync(ClientConnectedEventArgs args)
		{
			_logger.LogInformation($"Client connected: {args.ClientId}, Endpoint: {args.Endpoint}");
			return Task.CompletedTask;
		}

		private Task OnClientDisconnectedAsync(ClientDisconnectedEventArgs args)
		{
			_logger.LogInformation($"Client disconnected: {args.ClientId}, Reason: {args.DisconnectType}");
			return Task.CompletedTask;
		}

		private async Task OnInterceptingPublishAsync(InterceptingPublishEventArgs args)
		{
			try
			{
				var topic = args.ApplicationMessage.Topic;

				// Messages injected by the server itself (acks, status, HTTP publish endpoint) are never applied as data.
				if (args.ClientId == ServerClientId || !TryParseUpdateTopic(topic, out var collection, out var id))
				{
					return;
				}

				// Update payloads are not relayed: topics carry no group, so any subscriber could read them.
				args.ProcessPublish = false;

				if (string.IsNullOrEmpty(args.UserName))
				{
					_logger.LogWarning("Rejected update on {Topic} from anonymous client {ClientId}", topic, args.ClientId);
					return;
				}

				var payload = Encoding.UTF8.GetString(args.ApplicationMessage.Payload);

				// A fresh scope per message: LyriousRepository and its DbContext are scoped and not thread-safe.
				using var scope = _scopeFactory.CreateScope();
				var repository = scope.ServiceProvider.GetRequiredService<LyriousRepository>();
				repository.SetIdentity(new GenericIdentity(args.UserName));

				switch (collection)
				{
					case "songs":
						await ApplyUpdateAsync<Song>(repository, collection, id, payload, args.UserName);
						break;
					case "groups":
						await ApplyUpdateAsync<Group>(repository, collection, id, payload, args.UserName);
						break;
					case "setlists":
						await ApplyUpdateAsync<Setlist>(repository, collection, id, payload, args.UserName);
						break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing MQTT message");
			}
		}

		private Task OnMessageNotConsumedAsync(ApplicationMessageNotConsumedEventArgs args)
		{
			_logger.LogWarning($"Message not consumed - Topic: {args.ApplicationMessage.Topic}");
			return Task.CompletedTask;
		}

		#region Topic Processing Methods

		/// <summary>Matches lyrious/{collection}/{id}/update.</summary>
		private static bool TryParseUpdateTopic(string topic, out string collection, out Guid id)
		{
			var parts = topic.Split('/');
			collection = parts.Length == 4 ? parts[1] : string.Empty;
			id = Guid.Empty;

			return parts.Length == 4
			       && parts[0] == "lyrious"
			       && parts[3] == "update"
			       && Guid.TryParse(parts[2], out id);
		}

		private async Task ApplyUpdateAsync<TEntity>(LyriousRepository repository, string collection, Guid id, string payload, string userName)
			where TEntity : class, IEntity, new()
		{
			var entity = string.IsNullOrEmpty(payload) ? null : JsonSerializer.Deserialize<TEntity>(payload, JsonOptions);
			if (entity == null || entity.Id != id)
			{
				_logger.LogWarning("Rejected {Collection} update {Id} from {UserName}: payload missing or id mismatch", collection, id, userName);
				return;
			}

			if (!repository.CanWrite(entity))
			{
				_logger.LogWarning("Rejected {Collection} update {Id} from {UserName}: not allowed", collection, id, userName);
				return;
			}

			repository.Update(entity);
			await PublishMessageAsync($"lyrious/{collection}/{id}/updated", new { Success = true });
		}

		#endregion


		#region Helper Methods

		public async Task HandleWebSocketConnectionAsync(WebSocket webSocket, CancellationToken cancellationToken)
		{
			// In MQTTnet 5.0.1, you need to create an adapter for the WebSocket
			// This is a simplified example - in a real app, you would need to implement
			// the complete MQTT protocol over WebSockets

			_logger.LogInformation("WebSocket connection established for MQTT");

			// A buffer for receiving data
			var buffer = new byte[4096];

			try
			{
				// Keep the connection open until the token is cancelled
				while (!cancellationToken.IsCancellationRequested)
				{
					var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
							"Connection closed by the client", cancellationToken);
						break;
					}

					// Process the received data (this would be MQTT packets in a real implementation)
					// This is simplified and not a complete MQTT over WebSockets implementation
					_logger.LogInformation($"Received {result.Count} bytes from WebSocket");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error handling WebSocket connection for MQTT");

				// Try to close the connection if it's still open
				if (webSocket.State == WebSocketState.Open)
				{
					await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
						"Error processing WebSocket data", CancellationToken.None);
				}
			}
		}

		public async Task PublishMessageAsync(string topic, object payload)
		{
			try
			{
				// Convert the payload object to JSON
				var payloadString = JsonSerializer.Serialize(payload);

				// Create and publish the message
				var message = new MqttApplicationMessageBuilder()
					.WithTopic(topic)
					.WithPayload(payloadString)
					.WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
					.Build();

				await _mqttServer.InjectApplicationMessage(
					new InjectedMqttApplicationMessage(message)
					{
						SenderClientId = ServerClientId
					});

				_logger.LogInformation($"Published message to topic: {topic}");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"Error publishing message to topic: {topic}");
			}
		}

		private async Task SetupDefaultTopicsAsync()
		{
			// Publish initial status messages
			await PublishMessageAsync("lyrious/system/status", new
			{
				Status = "online",
				Timestamp = DateTime.UtcNow,
				Version = "1.0.0"
			});
		}

		#endregion
	}
}
