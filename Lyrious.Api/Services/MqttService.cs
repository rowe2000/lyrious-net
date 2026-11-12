using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Lyrious.Api.Services
{
	public class MqttService : IHostedService
	{
		private readonly MqttServer _mqttServer;
		private readonly ILogger<MqttService> _logger;
		private readonly LyriousRepository _repository;

		public MqttService(MqttServer mqttServer, ILogger<MqttService> logger, IServiceProvider serviceProvider)
		{
			_mqttServer = mqttServer;
			_logger = logger;

			// Create a scope to resolve scoped services
			var scope = serviceProvider.CreateScope();
			_repository = scope.ServiceProvider.GetRequiredService<LyriousRepository>();
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			// Set up server event handlers
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
			_mqttServer.ClientConnectedAsync -= OnClientConnectedAsync;
			_mqttServer.ClientDisconnectedAsync -= OnClientDisconnectedAsync;
			_mqttServer.InterceptingPublishAsync -= OnInterceptingPublishAsync;
			_mqttServer.ApplicationMessageNotConsumedAsync -= OnMessageNotConsumedAsync;

			// Stop the MQTT server
			await _mqttServer.StopAsync();

			_logger.LogInformation("MQTT server stopped");
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
				var payload = Encoding.UTF8.GetString(args.ApplicationMessage.Payload);

				_logger.LogInformation($"Message received - Topic: {topic}, Payload: {payload}");

				// Process messages based on topic patterns
				if (topic.StartsWith("lyrious/songs/"))
				{
					await ProcessSongMessageAsync(topic, payload);
				}
				else if (topic.StartsWith("lyrious/groups/"))
				{
					await ProcessGroupMessageAsync(topic, payload);
				}
				else if (topic.StartsWith("lyrious/setlists/"))
				{
					await ProcessSetlistMessageAsync(topic, payload);
				}
				// Add other topic patterns as needed
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

		private async Task ProcessSongMessageAsync(string topic, string payload)
		{
			// Example: Topic pattern: lyrious/songs/{id}/update
			var parts = topic.Split('/');

			if (parts.Length >= 4)
			{
				var songId = Guid.Parse(parts[2]);
				var action = parts[3];

				if (action == "update" && !string.IsNullOrEmpty(payload))
				{
					var song = JsonSerializer.Deserialize<Song>(payload);
					if (song != null)
					{
						_repository.Update(song);
						await PublishMessageAsync($"lyrious/songs/{songId}/updated", new { Success = true });
					}
				}
			}
		}

		private async Task ProcessGroupMessageAsync(string topic, string payload)
		{
			// Implement similar logic for Group entities
			var parts = topic.Split('/');

			if (parts.Length >= 4)
			{
				var groupId = Guid.Parse(parts[2]);
				var action = parts[3];

				if (action == "update" && !string.IsNullOrEmpty(payload))
				{
					var group = JsonSerializer.Deserialize<Group>(payload);
					if (group != null)
					{
						_repository.Update(group);
						await PublishMessageAsync($"lyrious/groups/{groupId}/updated", new { Success = true });
					}
				}
			}
		}

		private async Task ProcessSetlistMessageAsync(string topic, string payload)
		{
			// Implement similar logic for Setlist entities
			var parts = topic.Split('/');

			if (parts.Length >= 4)
			{
				var setlistId = Guid.Parse(parts[2]);
				var action = parts[3];

				if (action == "update" && !string.IsNullOrEmpty(payload))
				{
					var setlist = JsonSerializer.Deserialize<Setlist>(payload);
					if (setlist != null)
					{
						_repository.Update(setlist);
						await PublishMessageAsync($"lyrious/setlists/{setlistId}/updated", new { Success = true });
					}
				}
			}
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
						SenderClientId = "SERVER"
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
