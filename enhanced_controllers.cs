// -------------------------
// ENHANCED CONTROLLERS
// -------------------------

// Controllers/AuthController.cs
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var response = await _authService.LoginAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("generate-access-code")]
    [Authorize]
    public async Task<ActionResult<string>> GenerateAccessCode([FromBody] CreateAccessCodeRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var accessCode = await _authService.GenerateAccessCodeAsync(userId, request);
            return Ok(new { accessCode });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst("userId")?.Value ?? "0");
    }
}

// Controllers/DocumentController.cs
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly IDocumentService _documentService;

    public DocumentController(AppDbContext context, IWebHostEnvironment env, IDocumentService documentService)
    {
        _context = context;
        _env = env;
        _documentService = documentService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(
        IFormFile file, 
        [FromForm] int minimumTierLevel = 1,
        [FromForm] string category = "",
        [FromForm] string description = "",
        [FromForm] bool isConfidential = false)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("Invalid file.");

            var userId = GetCurrentUserId();
            var user = await _context.Users.FindAsync(userId);
            
            if (user == null)
                return Unauthorized();

            // Users can only set minimum tier level >= their own tier level
            if (minimumTierLevel < user.TierLevel)
                return BadRequest("Cannot set minimum tier level higher than your own tier level.");

            var uploadsPath = Path.Combine(_env.ContentRootPath, "Uploads");
            if (!Directory.Exists(uploadsPath))
                Directory.CreateDirectory(uploadsPath);

            // Generate unique filename to prevent conflicts
            var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsPath, uniqueFileName);

            // Calculate file hash for integrity
            string fileHash;
            using (var stream = file.OpenReadStream())
            {
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    fileHash = Convert.ToBase64String(hashBytes);
                }
            }

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var document = new Document
            {
                FileName = file.FileName,
                FilePath = filePath,
                FileHash = fileHash,
                FileSize = file.Length,
                ContentType = file.ContentType,
                UploadedByUserId = userId,
                MinimumTierLevel = minimumTierLevel,
                Category = category,
                Description = description,
                IsConfidential = isConfidential
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "Upload successful", 
                documentId = document.Id,
                fileName = document.FileName
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Upload failed: {ex.Message}" });
        }
    }

    [HttpGet("list")]
    public async Task<ActionResult<List<object>>> List()
    {
        try
        {
            var userId = GetCurrentUserId();
            var documents = await _documentService.GetAccessibleDocuments(userId);

            var result = documents.Select(d => new
            {
                d.Id,
                d.FileName,
                d.Category,
                d.Description,
                d.UploadDate,
                d.FileSize,
                d.IsConfidential,
                d.MinimumTierLevel,
                UploadedBy = d.UploadedByUser.FullName
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("download/{documentId}")]
    public async Task<IActionResult> Download(int documentId)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            if (!await _documentService.CanUserAccessDocument(userId, documentId))
                return Forbid("You don't have permission to access this document.");

            var document = await _context.Documents.FindAsync(documentId);
            if (document == null || !document.IsActive)
                return NotFound("Document not found.");

            if (!System.IO.File.Exists(document.FilePath))
                return NotFound("Physical file not found.");

            // Log access
            await _documentService.LogDocumentAccess(userId, documentId, "Download");

            var fileBytes = await System.IO.File.ReadAllBytesAsync(document.FilePath);
            return File(fileBytes, document.ContentType ?? "application/octet-stream", document.FileName);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("view/{documentId}")]
    public async Task<IActionResult> ViewDetails(int documentId)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            if (!await _documentService.CanUserAccessDocument(userId, documentId))
                return Forbid("You don't have permission to access this document.");

            var document = await _context.Documents
                .Include(d => d.UploadedByUser)
                .FirstOrDefaultAsync(d => d.Id == documentId && d.IsActive);

            if (document == null)
                return NotFound("Document not found.");

            // Log access
            await _documentService.LogDocumentAccess(userId, documentId, "View");

            var result = new
            {
                document.Id,
                document.FileName,
                document.Category,
                document.Description,
                document.UploadDate,
                document.FileSize,
                document.ContentType,
                document.IsConfidential,
                document.MinimumTierLevel,
                UploadedBy = document.UploadedByUser.FullName,
                UploadedByDepartment = document.UploadedByUser.Department
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{documentId}")]
    public async Task<IActionResult> DeleteDocument(int documentId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FindAsync(userId);
            var document = await _context.Documents.FindAsync(documentId);

            if (document == null)
                return NotFound("Document not found.");

            // Only the uploader or higher tier users can delete documents
            if (document.UploadedByUserId != userId && user.TierLevel > document.MinimumTierLevel)
                return Forbid("You don't have permission to delete this document.");

            // Soft delete
            document.IsActive = false;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Document deleted successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst("userId")?.Value ?? "0");
    }
}

// Controllers/UserController.cs
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly AppDbContext _context;

    public UserController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<object>> GetProfile()
    {
        try
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.ParentUser)
                .Include(u => u.SubordinateUsers)
                .Include(u => u.GeneratedAccessCodes)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound("User not found.");

            var result = new
            {
                user.Id,
                user.Username,
                user.Email,
                user.FullName,
                user.TierLevel,
                user.Department,
                user.CreatedDate,
                ParentUser = user.ParentUser?.FullName,
                SubordinateCount = user.SubordinateUsers.Count,
                ActiveAccessCodes = user.GeneratedAccessCodes
                    .Where(ac => !ac.IsUsed && ac.ExpiryDate > DateTime.UtcNow)
                    .Select(ac => new
                    {
                        ac.Code,
                        ac.TargetTierLevel,
                        ac.Department,
                        ac.ExpiryDate,
                        ac.MaxUses,
                        ac.CurrentUses
                    })
                    .ToList()
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("subordinates")]
    public async Task<ActionResult<List<object>>> GetSubordinates()
    {
        try
        {
            var userId = GetCurrentUserId();
            var subordinates = await _context.Users
                .Where(u => u.ParentUserId == userId && u.IsActive)
                .OrderBy(u => u.TierLevel)
                .ThenBy(u => u.FullName)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.FullName,
                    u.TierLevel,
                    u.Department,
                    u.CreatedDate,
                    DocumentCount = u.UploadedDocuments.Count(d => d.IsActive)
                })
                .ToListAsync();

            return Ok(subordinates);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("organization-tree")]
    public async Task<ActionResult<object>> GetOrganizationTree()
    {
        try
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FindAsync(userId);
            
            if (user == null)
                return NotFound();

            // Get all users at or below current user's tier level
            var users = await _context.Users
                .Where(u => u.TierLevel >= user.TierLevel && u.IsActive)
                .Include(u => u.SubordinateUsers)
                .OrderBy(u => u.TierLevel)
                .ToListAsync();

            var tree = BuildUserTree(users, user.TierLevel <= 2 ? (int?)null : userId);
            return Ok(tree);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("deactivate/{targetUserId}")]
    public async Task<IActionResult> DeactivateUser(int targetUserId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var currentUser = await _context.Users.FindAsync(userId);
            var targetUser = await _context.Users.FindAsync(targetUserId);

            if (currentUser == null || targetUser == null)
                return NotFound("User not found.");

            // Can only deactivate users in lower tiers
            if (targetUser.TierLevel <= currentUser.