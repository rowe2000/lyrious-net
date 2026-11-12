using System.Text;
using System.Text.Json;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib;

public class ApiClient : IRepository, IDisposable
{
	private string token = "";
	private readonly JsonSerializerOptions options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true, // Ignore case for property names
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // Use camelCase for JSON
		ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, // Handle circular references
		IgnoreReadOnlyProperties = true,
		WriteIndented = true,
		AllowTrailingCommas = true,
	};

	// Cookies off: the API authenticates with the bearer token only.
	private HttpClient? client = new(new HttpClientHandler { UseCookies = false });
	private string baseUrl = "";

	/// <summary>The current bearer token, empty when logged out (used to authenticate the hub connection).</summary>
	public string AccessToken => token;

	/// <summary>
	/// This client's hub connection id, sent with each request so the API doesn't
	/// push the client's own changes back to it.
	/// </summary>
	public string? HubConnectionId
	{
		set
		{
			if (IsDisposed)
			{
				return;
			}

			client!.DefaultRequestHeaders.Remove(HubConnectionIdHeader);
			if (!string.IsNullOrEmpty(value))
			{
				client.DefaultRequestHeaders.Add(HubConnectionIdHeader, value);
			}
		}
	}

	private const string HubConnectionIdHeader = "X-Lyrious-ConnectionId";

	private string Token
	{
		get => token;
		set
		{
			token = value;
			if (!IsDisposed)
			{
				client!.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
					? null
					: new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
			}
		}
	}

	public string BaseUrl
	{
		get => baseUrl;
		set
		{
			if (baseUrl == value)
			{
				return;
			}
			
			baseUrl = value;

			if (string.IsNullOrWhiteSpace(BaseUrl) || IsDisposed)
			{
				return;
			}
			
			client!.BaseAddress = new Uri(BaseUrl);
		}
	}

	// New Get method
	public void Change<TEntity>(object sender, ChangedArgs<TEntity> args) where TEntity : class, IEntity, new()
	{
		switch (args.ChangedEnum)
		{
			case ChangedEnum.Add:
			case ChangedEnum.Insert:
			case ChangedEnum.Update:
				Update(args.Values);
				break;

			case ChangedEnum.Remove:
			case ChangedEnum.Clear:
				Remove<TEntity>(args.Values.Select(o => o.Id));
				break;

			case ChangedEnum.Move:
			default:
				break;
		}
	}

	public IEnumerable<TEntity> Get<TEntity>() where TEntity : class, IEntity, new()
	{
		return Get<TEntity>(string.Empty);
	}

	public IEnumerable<TEntity> Get<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		// The API has no batch-by-id route; fetch the (access-filtered) list and pick the ids.
		var idSet = ids.ToHashSet();
		return Get<TEntity>(string.Empty).Where(o => idSet.Contains(o.Id)).ToArray();
	}
	public IEnumerable<TEntity> Get<TEntity>(DateTime commit) where TEntity : class, IEntity, new()
	{
		return Get<TEntity>($"/commit={commit.ToFileTimeUtc()}");
	}

	public IEnumerable<TEntity> Get<TEntity>(Func<TEntity, bool> p) where TEntity : class, IEntity, new()
	{
		return Get<TEntity>(string.Empty).Where(p);
	}

	private IEnumerable<TEntity> Get<TEntity>(string getParams) where TEntity : class, IEntity, new()
	{
		try
		{
			// Send GET request
			var entityName = typeof(TEntity).Name;
			var requestUri = $"/api/{entityName}{getParams}";
			var response = client.GetAsync(requestUri).Result;

			// Check if the response is successful
			if (response.IsSuccessStatusCode)
			{
				var responseData = response.Content.ReadAsStringAsync().Result;
				Console.WriteLine("Data retrieved successfully!");

				return JsonSerializer.Deserialize<IEnumerable<TEntity>>(responseData, options) ?? throw new Exception();
			}

			Console.WriteLine("Failed to retrieve data. Status Code: " + response.StatusCode);
		}
		catch (Exception ex)
		{
			if (IsDisposed)
			{
				Console.WriteLine($"Client is disposed: {ex.Message}");
			}

			Console.WriteLine("An error occurred: " + ex.Message);
		}

		return [];
	}

	public TEntity? Update<TEntity>(TEntity entity) where TEntity : class, IEntity, new()
	{
		return Update([entity]).FirstOrDefault();
	}
	public IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> entities) where TEntity : class, IEntity, new()
	{
		var entityName = typeof(TEntity).Name;
		var requestUri = $"/api/{entityName}";
		try
		{
			// Serialize the LoginModel to JSON
			var jsonData = JsonSerializer.Serialize(entities, options);
			var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

			// Send POST request
			var response = client.PostAsync(requestUri, content).Result;

			// Check if the response is successful
			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"Succeeded to upload {entities.Count()} {entityName} entities to {BaseUrl}");
				var responseData = response.Content.ReadAsStringAsync().Result;
				Console.WriteLine("  Response from API: " + responseData);
				entities.ForEach(o => Console.WriteLine($"    {o}"));
			}
			else
			{
				Console.WriteLine($"Failed to upload {entityName} entities to {BaseUrl}:");
				Console.WriteLine("  Status Code: " + response.StatusCode);
				entities?.ForEach(o => Console.WriteLine($"    {o}"));
				Console.WriteLine(jsonData);
			}
		}
		catch (Exception ex)
		{
			if (IsDisposed)
			{
				Console.WriteLine($"Client is disposed: {ex.Message}");
			}

			Console.WriteLine($"Failed to upload {entityName} entities to {BaseUrl}: {ex.Message}");
		}

		return [];
	}

	public void Remove<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		var entityName = typeof(TEntity).Name;
		var idList = string.Join(",", ids);
		var requestUri = $"/api/{entityName}/ids={idList}";
		try
		{
			// Send GET request
			_ = client.DeleteAsync(requestUri);
		}
		catch (Exception ex)
		{
			if (IsDisposed)
			{
				Console.WriteLine($"Client is disposed: {ex.Message}");
			}

			Console.WriteLine($"Exception: {ex.Message}");
		}
	}

	public async Task<Member?> RegisterAsync(RegisterModel model)
	{
		// Registering doesn't change who is logged in, so the current token is left alone.
		var response = await Post(model, "/api/auth/register");
		if (response is not { IsSuccessStatusCode: true })
		{
			return null;
		}

		var json = await response.Content.ReadAsStringAsync();
		var tokenModel = JsonSerializer.Deserialize<TokenModel>(json, options);

		Console.WriteLine($"User {model.UserName} registered successfully.");

		return tokenModel?.Member;
	}

	public async Task<Member?> LoginAsync(LoginModel model)
	{
		Token = string.Empty;

		var response = await Post(model, "/api/auth/login");
		if (response is not { IsSuccessStatusCode: true })
		{
			return null;
		}

		var json = await response.Content.ReadAsStringAsync();
		var tokenModel = JsonSerializer.Deserialize<TokenModel>(json, options);
		Token = tokenModel?.Token ?? string.Empty;

		Console.WriteLine($"User {model.Username} logged in successfully.");

		return string.IsNullOrEmpty(Token) ? null : tokenModel?.Member;
	}

	private async Task Post(string requestUri)
	{
		await Post<object>(null, requestUri);
	}
	private async Task<HttpResponseMessage?> Post<TPost>(TPost? entities, string requestUri)
	{
		HttpResponseMessage? response = null;
		StringContent? content = null;

		try
		{
			// Serialize the LoginModel to JSON
			if (entities != null)
			{
				var jsonData = JsonSerializer.Serialize(entities);
				content = new StringContent(jsonData, Encoding.UTF8, "application/json");
			}

			// Send POST request to the login endpoint
			response = await client.PostAsync(requestUri, content);

			return response;
		}
		catch (Exception ex)
		{
			Console.WriteLine(IsDisposed ? $"Client is disposed: {ex.Message}" : $"Exception: {ex.Message}");
			throw;
		}
		finally
		{
			if (response is { IsSuccessStatusCode: true })
			{
				Console.WriteLine($"{requestUri} succeeded.");
			}
			else if (response != null)
			{
				ThrowIfNotSuccessStatusCode(response);
			}

		}
	}

	private static void ThrowIfNotSuccessStatusCode(HttpResponseMessage? response)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		var message = $"Uri {response.RequestMessage?.RequestUri} failed -> {response.StatusCode} {response.ReasonPhrase}";
		Console.WriteLine(message);
		//throw new Exception(message);
	}

	public async Task LogoutAsync()
	{
		await Post("/api/auth/logout");
		Token = string.Empty;
		Console.WriteLine("User logged out successfully.");
	}

	public async Task<Member?> FindMemberAsync(string query)
	{
		var response = await client!.GetAsync($"/api/member/find?query={Uri.EscapeDataString(query)}");
		if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return null;
		}

		response.EnsureSuccessStatusCode();
		var json = await response.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize<Member>(json, options);
	}

	public async Task<string> Ping<TEntity>()
	{
		var requestUri = $"/api/{typeof(TEntity).Name}/ping";

		try
		{
			// Send POST request to the logout endpoint
			var response = await client.PostAsync(requestUri, null);

			if (response.IsSuccessStatusCode)
			{
				return await response.Content.ReadAsStringAsync();
			}

			var error = await response.Content.ReadAsStringAsync();
			return $"Error: {error}";
		}
		catch (Exception ex)
		{
			if (IsDisposed)
			{
				Console.WriteLine($"Client is disposed: {ex.Message}");
			}

			return $"Exception: {ex.Message}";
		}
	}

	public void Dispose()
	{
		client?.Dispose();
		client = null;
	}

	public bool IsDisposed => client == null;
}
