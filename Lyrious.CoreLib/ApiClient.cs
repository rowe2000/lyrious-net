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

	private HttpClient? client = new();
	private string baseUrl = "";

	public string BaseUrl
	{
		get => baseUrl;
		set
		{
			if (baseUrl == value)
				return;

			baseUrl = value;

			if (string.IsNullOrWhiteSpace(BaseUrl) || IsDisposed)
				return;

			client.BaseAddress = new Uri(BaseUrl);
			client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
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
		var idList = string.Join(",", ids);
		return Get<TEntity>($"/ids={idList}");
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
		try
		{
			// Serialize the LoginModel to JSON
			var jsonData = JsonSerializer.Serialize(entities, options);
			var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

			// Send POST request
			var requestUri = $"/api/{entityName}";
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
		try
		{
			var idList = string.Join(",", ids);

			// Send GET request
			var entityName = typeof(TEntity).Name;
			var requestUri = $"/api/{entityName}/ids={idList}";
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
		try
		{
			// Serialize the RegisterModel to JSON
			var jsonData = JsonSerializer.Serialize(model);
			var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

			// Send POST request to the register endpoint
			var requestUri = $"/api/auth/register";
			var response = await client.PostAsync(requestUri, content);

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine("User registered successfully.");
				var json = await response.Content.ReadAsStringAsync();
				var tokenModel = JsonSerializer.Deserialize<TokenModel>(json, options);
				token = tokenModel?.Token ?? string.Empty;

				return tokenModel?.Member;
			}

			var error = await response.Content.ReadAsStringAsync();
			Console.WriteLine($"User registration failed. Error: {error}");
		}
		catch (Exception ex)
		{
			if (IsDisposed)
			{
				Console.WriteLine($"Client is disposed: {ex.Message}");
			}

			Console.WriteLine($"Exception: {ex.Message}");
		}

		return null;
	}

	public async Task<Member?> LoginAsync(LoginModel model)
	{
		try
		{
			// Serialize the LoginModel to JSON
			var jsonData = JsonSerializer.Serialize(model);
			var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

			// Send POST request to the login endpoint
			var requestUri = $"/api/auth/login";
			var response = await client.PostAsync(requestUri, content);

			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				var tokenModel = JsonSerializer.Deserialize<TokenModel>(json, options);
				token = tokenModel?.Token ?? string.Empty;

				Console.WriteLine("User logged in successfully.");
				return tokenModel?.Member;
			}

			var error = await response.Content.ReadAsStringAsync();
			Console.WriteLine($"User login failed. Error: {error}");
		}
		catch (Exception ex)
		{
			if (IsDisposed)
			{
				Console.WriteLine($"Client is disposed: {ex.Message}");
			}

			Console.WriteLine($"Exception: {ex.Message}");
		}
		return null;
	}

	public async Task<string> LogoutAsync()
	{
		try
		{
			// Send POST request to the logout endpoint
			var requestUri = $"/api/auth/logout";
			var response = await client.PostAsync(requestUri, null);

			if (response.IsSuccessStatusCode)
			{
				return "User registered successfully.";
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

	public async Task<string> Ping<TEntity>()
	{
		try
		{
			// Send POST request to the logout endpoint
			var requestUri = $"/api/{typeof(TEntity).Name}/ping";
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
