using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text;
using Microsoft.Maui.Authentication.WebUI;

namespace NoPaperLeak;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://localhost:7001/api"; // Update with your API URL
    private string _authToken;

    // Authentication Properties
    private bool _isAuthenticated;
    private string _username = "";
    private string _password = "";
    private string _email = "";
    private string _fullName = "";
    private string _department = "";
    private string _accessCode = "";

    // User Profile Properties
    private string _userProfile = "";
    private int _userTier;
    private string _userDepartment = "";

    // Access Code Properties
    private ObservableCollection<string> _availableTierLevels = new();
    private int _selectedTierLevel;
    private string _codeDepartment = "";
    private int _maxUses = 1;
    private int _expiryDays = 30;
    private ObservableCollection<AccessCodeInfo> _accessCodes = new();

    // Document Properties
    private FileResult _selectedFile;
    private string _selectedFileName = "No file selected";
    private ObservableCollection<int> _minTierLevels = new();
    private int _selectedMinTierLevel = 1;
    private string _documentCategory = "";
    private string _documentDescription = "";
    private bool _isConfidential;
    private ObservableCollection<DocumentInfo> _documents = new();

    // Organization Properties
    private bool _canViewOrganization;
    private ObservableCollection<UserInfo> _organizationTree = new();

    public MainPage()
    {
        InitializeComponent();
        _httpClient = new HttpClient();
        BindingContext = this;
        
        InitializeCollections();
    }

    private void InitializeCollections()
    {
        // Initialize tier levels (users can create codes for tiers below them)
        for (int i = 2; i <= 10; i++)
        {
            AvailableTierLevels.Add(i.ToString());
            MinTierLevels.Add(i);
        }
        MinTierLevels.Insert(0, 1);
    }

    #region Properties

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set
        {
            _isAuthenticated = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotAuthenticated));
            OnPropertyChanged(nameof(CanViewOrganization));
        }
    }

    public bool IsNotAuthenticated => !IsAuthenticated;

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); }
    }

    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); }
    }

    public string FullName
    {
        get => _fullName;
        set { _fullName = value; OnPropertyChanged(); }
    }

    public string Department
    {
        get => _department;
        set { _department = value; OnPropertyChanged(); }
    }

    public string AccessCode
    {
        get => _accessCode;
        set { _accessCode = value; OnPropertyChanged(); }
    }

    public string UserProfile
    {
        get => _userProfile;
        set { _userProfile = value; OnPropertyChanged(); }
    }

    public int UserTier
    {
        get => _userTier;
        set 
        { 
            _userTier = value; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanViewOrganization));
            UpdateAvailableTierLevels();
        }
    }

    public string UserDepartment
    {
        get => _userDepartment;
        set { _userDepartment = value; OnPropertyChanged(); }
    }

    public bool CanViewOrganization => IsAuthenticated && UserTier <= 3;

    public ObservableCollection<string> AvailableTierLevels
    {
        get => _availableTierLevels;
        set { _availableTierLevels = value; OnPropertyChanged(); }
    }

    public int SelectedTierLevel
    {
        get => _selectedTierLevel;
        set { _selectedTierLevel = value; OnPropertyChanged(); }
    }

    public string CodeDepartment
    {
        get => _codeDepartment;
        set { _codeDepartment = value; OnPropertyChanged(); }
    }

    public int MaxUses
    {
        get => _maxUses;
        set { _maxUses = value; OnPropertyChanged(); }
    }

    public int ExpiryDays
    {
        get => _expiryDays;
        set { _expiryDays = value; OnPropertyChanged(); }
    }

    public ObservableCollection<AccessCodeInfo> AccessCodes
    {
        get => _accessCodes;
        set { _accessCodes = value; OnPropertyChanged(); }
    }

    public string SelectedFileName
    {
        get => _selectedFileName;
        set { _selectedFileName = value; OnPropertyChanged(); }
    }

    public ObservableCollection<int> MinTierLevels
    {
        get => _minTierLevels;
        set { _minTierLevels = value; OnPropertyChanged(); }
    }

    public int SelectedMinTierLevel
    {
        get => _selectedMinTierLevel;
        set { _selectedMinTierLevel = value; OnPropertyChanged(); }
    }

    public string DocumentCategory
    {
        get => _documentCategory;
        set { _documentCategory = value; OnPropertyChanged(); }
    }

    public string DocumentDescription
    {
        get => _documentDescription;
        set { _documentDescription = value; OnPropertyChanged(); }
    }

    public bool IsConfidential
    {
        get => _isConfidential;
        set { _isConfidential = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DocumentInfo> Documents
    {
        get => _documents;
        set { _documents = value; OnPropertyChanged(); }
    }

    public ObservableCollection<UserInfo> OrganizationTree
    {
        get => _organizationTree;
        set { _organizationTree = value; OnPropertyChanged(); }
    }

    #endregion

    #region Commands

    public Command LoginCommand => new Command(async () => await LoginAsync());
    public Command RegisterCommand => new Command(async () => await RegisterAsync());
    public Command LogoutCommand => new Command(Logout);
    public Command GenerateAccessCodeCommand => new Command(async () => await GenerateAccessCodeAsync());
    public Command SelectFileCommand => new Command(async () => await SelectFileAsync());
    public Command UploadDocumentCommand => new Command(async () => await UploadDocumentAsync());
    public Command RefreshDocumentsCommand => new Command(async () => await LoadDocumentsAsync());
    public Command<int> ViewDocumentCommand => new Command<int>(async (id) => await ViewDocumentAsync(id));
    public Command<int> DownloadDocumentCommand => new Command<int>(async (id) => await DownloadDocumentAsync(id));
    public Command LoadOrganizationCommand => new Command(async () => await LoadOrganizationTreeAsync());

    #endregion

    #region Methods

    private void UpdateAvailableTierLevels()
    {
        AvailableTierLevels.Clear();
        for (int i = UserTier + 1; i <= 10; i++)
        {
            AvailableTierLevels.Add(i.ToString());
        }
        
        if (AvailableTierLevels.Count > 0)
            SelectedTierLevel = UserTier + 1;
    }

    private async Task LoginAsync()
    {
        try
        {
            var loginData = new
            {
                username = Username,
                password = Password
            };

            var json = JsonSerializer.Serialize(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{BaseUrl}/auth/login", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                _authToken = authResponse.Token;
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authToken);
                
                UserProfile = authResponse.FullName;
                UserTier = authResponse.TierLevel;
                UserDepartment = authResponse.Department;
                IsAuthenticated = true;
                
                // Load initial data
                await LoadDocumentsAsync();
                await LoadUserProfileAsync();
                
                await DisplayAlert("Success", "Login successful!", "OK");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Login failed: {errorContent}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Login error: {ex.Message}", "OK");
        }
    }

    private async Task RegisterAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password) || 
                string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(FullName))
            {
                await DisplayAlert("Error", "Please fill in all required fields", "OK");
                return;
            }

            var registerData = new
            {
                username = Username,
                password = Password,
                email = Email,
                fullName = FullName,
                department = Department,
                accessCode = AccessCode
            };

            var json = JsonSerializer.Serialize(registerData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{BaseUrl}/auth/register", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                _authToken = authResponse.Token;
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authToken);
                
                UserProfile = authResponse.FullName;
                UserTier = authResponse.TierLevel;
                UserDepartment = authResponse.Department;
                IsAuthenticated = true;
                
                await LoadDocumentsAsync();
                await LoadUserProfileAsync();
                
                await DisplayAlert("Success", "Registration successful!", "OK");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Registration failed: {errorContent}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Registration error: {ex.Message}", "OK");
        }
    }

    private void Logout()
    {
        _authToken = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        IsAuthenticated = false;
        
        // Clear data
        Documents.Clear();
        AccessCodes.Clear();
        OrganizationTree.Clear();
        
        // Reset form fields
        Username = Password = Email = FullName = Department = AccessCode = "";
        UserProfile = UserDepartment = "";
        UserTier = 0;
    }

    private async Task LoadUserProfileAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/user/profile");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var profile = JsonSerializer.Deserialize<UserProfile>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                AccessCodes.Clear();
                foreach (var code in profile.ActiveAccessCodes)
                {
                    AccessCodes.Add(new AccessCodeInfo
                    {
                        Code = code.Code,
                        Details = $"Tier {code.TargetTierLevel} | {code.Department} | Expires: {code.ExpiryDate:MM/dd/yyyy} | Uses: {code.CurrentUses}/{code.MaxUses}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load profile: {ex.Message}", "OK");
        }
    }

    private async Task GenerateAccessCodeAsync()
    {
        try
        {
            if (SelectedTierLevel <= UserTier)
            {
                await DisplayAlert("Error", "You can only create access codes for lower tier levels", "OK");
                return;
            }

            var requestData = new
            {
                targetTierLevel = SelectedTierLevel,
                department = string.IsNullOrWhiteSpace(CodeDepartment) ? UserDepartment : CodeDepartment,
                maxUses = MaxUses,
                expiryDays = ExpiryDays
            };

            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{BaseUrl}/auth/generate-access-code", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseContent);
                
                await DisplayAlert("Success", $"Access code generated: {result["accessCode"]}", "OK");
                await LoadUserProfileAsync(); // Refresh access codes
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Failed to generate access code: {errorContent}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Error generating access code: {ex.Message}", "OK");
        }
    }

    private async Task SelectFileAsync()
    {
        try
        {
            var result = await FilePicker.PickAsync();
            if (result != null)
            {
                _selectedFile = result;
                SelectedFileName = result.FileName;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"File selection failed: {ex.Message}", "OK");
        }
    }

    private async Task UploadDocumentAsync()
    {
        try
        {
            if (_selectedFile == null)
            {
                await DisplayAlert("Error", "Please select a file first", "OK");
                return;
            }

            using var stream = await _selectedFile.OpenReadAsync();
            var content = new MultipartFormDataContent();
            content.Add(new StreamContent(stream), "file", _selectedFile.FileName);
            content.Add(new StringContent(SelectedMinTierLevel.ToString()), "minimumTierLevel");
            content.Add(new StringContent(DocumentCategory ?? ""), "category");
            content.Add(new StringContent(DocumentDescription ?? ""), "description");
            content.Add(new StringContent(IsConfidential.ToString()), "isConfidential");

            var response = await _httpClient.PostAsync($"{BaseUrl}/document/upload", content);
            
            if (response.IsSuccessStatusCode)
            {
                await DisplayAlert("Success", "Document uploaded successfully!", "OK");
                
                // Reset form
                _selectedFile = null;
                SelectedFileName = "No file selected";
                DocumentCategory = "";
                DocumentDescription = "";
                IsConfidential = false;
                SelectedMinTierLevel = 1;
                
                // Refresh documents list
                await LoadDocumentsAsync();
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Upload failed: {errorContent}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Upload error: {ex.Message}", "OK");
        }
    }

    private async Task LoadDocumentsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/document/list");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var documents = JsonSerializer.Deserialize<List<DocumentInfo>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                Documents.Clear();
                foreach (var doc in documents)
                {
                    Documents.Add(doc);
                }
            }
            else
            {
                await DisplayAlert("Error", "Failed to load documents", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Error loading documents: {ex.Message}", "OK");
        }
    }

    private async Task ViewDocumentAsync(int documentId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/document/{documentId}/details");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var docDetails = JsonSerializer.Deserialize<DocumentDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                var message = $"Document: {docDetails.FileName}\n" +
                             $"Category: {docDetails.Category}\n" +
                             $"Description: {docDetails.Description}\n" +
                             $"Uploaded by: {docDetails.UploadedBy}\n" +
                             $"Upload Date: {docDetails.UploadDate:MM/dd/yyyy HH:mm}\n" +
                             $"File Size: {FormatFileSize(docDetails.FileSize)}\n" +
                             $"Minimum Tier: {docDetails.MinimumTierLevel}\n" +
                             $"Confidential: {(docDetails.IsConfidential ? "Yes" : "No")}\n" +
                             $"Access Count: {docDetails.AccessCount}";
                
                await DisplayAlert("Document Details", message, "OK");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Failed to load document details: {errorContent}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Error viewing document: {ex.Message}", "OK");
        }
    }

    private async Task DownloadDocumentAsync(int documentId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/document/{documentId}/download");
            
            if (response.IsSuccessStatusCode)
            {
                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "document";
                
                // Save file to Downloads folder
                var downloadsPath = Path.Combine(FileSystem.Current.AppDataDirectory, "Downloads");
                Directory.CreateDirectory(downloadsPath);
                
                var filePath = Path.Combine(downloadsPath, fileName);
                await File.WriteAllBytesAsync(filePath, fileBytes);
                
                await DisplayAlert("Success", $"Document downloaded to: {filePath}", "OK");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Download failed: {errorContent}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Download error: {ex.Message}", "OK");
        }
    }

    private async Task LoadOrganizationTreeAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/organization/tree");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var users = JsonSerializer.Deserialize<List<UserInfo>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                OrganizationTree.Clear();
                foreach (var user in users.OrderBy(u => u.TierLevel).ThenBy(u => u.Department).ThenBy(u => u.FullName))
                {
                    OrganizationTree.Add(user);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Failed to load organization tree: {errorContent}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Error loading organization: {ex.Message}", "OK");
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

#region Model Classes

public class AuthResponse
{
    public string Token { get; set; }
    public string FullName { get; set; }
    public int TierLevel { get; set; }
    public string Department { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
}

public class UserProfile
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    public string Department { get; set; }
    public int TierLevel { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<AccessCodeData> ActiveAccessCodes { get; set; } = new();
}

public class AccessCodeData
{
    public string Code { get; set; }
    public int TargetTierLevel { get; set; }
    public string Department { get; set; }
    public int MaxUses { get; set; }
    public int CurrentUses { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsActive { get; set; }
}

public class AccessCodeInfo
{
    public string Code { get; set; }
    public string Details { get; set; }
}

public class DocumentInfo
{
    public int Id { get; set; }
    public string FileName { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
    public string UploadedBy { get; set; }
    public DateTime UploadDate { get; set; }
    public long FileSize { get; set; }
    public int MinimumTierLevel { get; set; }
    public bool IsConfidential { get; set; }
    public int AccessCount { get; set; }

    public string UploadInfo => $"By {UploadedBy} on {UploadDate:MM/dd/yyyy} | Tier {MinimumTierLevel}+ | {(IsConfidential ? "Confidential" : "Standard")}";
}

public class DocumentDetails
{
    public int Id { get; set; }
    public string FileName { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
    public string UploadedBy { get; set; }
    public DateTime UploadDate { get; set; }
    public long FileSize { get; set; }
    public int MinimumTierLevel { get; set; }
    public bool IsConfidential { get; set; }
    public int AccessCount { get; set; }
    public string ContentType { get; set; }
    public string FilePath { get; set; }
}

public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    public string Department { get; set; }
    public int TierLevel { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLogin { get; set; }
    public bool IsActive { get; set; }

    public string DisplayName => $"{FullName} ({Username})";
    public string TierInfo => $"Tier {TierLevel} | {Department} | Last Login: {LastLogin:MM/dd/yyyy}";
}

#endregion
