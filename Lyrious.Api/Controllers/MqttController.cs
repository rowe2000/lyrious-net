using Lyrious.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers
{
	[ApiController]
	[Authorize]
	[Route("api/[controller]")]
	public class MqttController : ControllerBase
	{
		private readonly MqttService _mqttService;
		private readonly ILogger<MqttController> _logger;

		public MqttController(
			MqttService mqttService,
			ILogger<MqttController> logger)
		{
			_mqttService = mqttService;
			_logger = logger;
		}

		[HttpPost("publish")]
		public async Task<IActionResult> PublishMessage([FromBody] MqttPublishRequest request)
		{
			if (string.IsNullOrEmpty(request.Topic))
			{
				return BadRequest("Topic is required");
			}

			try
			{
				await _mqttService.PublishMessageAsync(request.Topic, request.Payload);
				return Ok(new { Success = true, Message = "Message published successfully" });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error publishing MQTT message");
				return StatusCode(500, new { Success = false, Error = "Failed to publish message" });
			}
		}
	}

	public class MqttPublishRequest
	{
		public string Topic { get; set; } = string.Empty;
		public object? Payload { get; set; }
	}
}