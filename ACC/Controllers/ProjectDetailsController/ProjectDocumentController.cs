﻿using ACC.Controllers.Viewers;
using ACC.Services;
using ACC.ViewModels.ProjectDocumentsVM;
using BusinessLogic.Repository.RepositoryInterfaces;
using DataLayer;
using DataLayer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NuGet.Packaging.Signing;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Threading.Tasks;
using static NuGet.Packaging.PackagingConstants;

namespace ACC.Controllers.ProjectDetailsController
{
    public class ProjectDocumentController : Controller
    {
        public static int id;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IProjetcRepository _projetcRepository;
        private readonly IFolderRepository _folderRepository;
        private readonly IDocumentVersionRepository _documentVersionRepository;
        private readonly IDocumentRepository _documentRepository;
        private readonly string _uploadsPath = Path.Combine("wwwroot", "Uploads");
        private readonly string _convertedPath;
        public ProjectDocumentController(AppDbContext context, IWebHostEnvironment env, IProjetcRepository projetcRepository, IFolderRepository folderRepository, IDocumentVersionRepository documentVersionRepository, IDocumentRepository documentRepository)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _projetcRepository = projetcRepository;
            _folderRepository = folderRepository;
            _documentVersionRepository = documentVersionRepository;
            _documentRepository = documentRepository;
            _convertedPath = Path.Combine(_env.WebRootPath, "converted");
        }

        [HttpGet]
        public async Task<IActionResult> Index(int Id)
        {
            if (Id == 0)
            {
                Id = id; // Ensure 'id' is defined; consider validating or clarifying its source
            }

            try
            {
                // Check if project exists
                var project = _projetcRepository.GetById(Id); // Typo: '_projetcRepository' should be '_projectRepository'
                if (project == null)
                {
                    return NotFound("Project not found.");
                }

                // Get all root folders for the project
                var folders = await _folderRepository.GetAllQueryable()
                    .Where(f => f.ParentFolderId == null && f.ProjectId == Id)
                    .Include(f => f.Documents) // Include documents for root folders
                    .ToListAsync();

                // Recursively load subfolders and their documents
                foreach (var folder in folders)
                {
                    await LoadSubFoldersAsync(folder, _folderRepository);
                }

                ViewBag.ProjectId = Id;
                id = Id; // Ensure 'id' is defined; consider clarifying its purpose
                ViewBag.AllFolders = _folderRepository.GetAll();

                if (!folders.Any())
                {
                    // Create default folders if none exist
                    var workInProgress = new Folder
                    {
                        Name = "Work In Progress",
                        ProjectId = Id,
                        SubFolders = new List<Folder>(),
                        Documents = new List<Document>(),
                        CreatedBy = "System"
                    };
                    var shared = new Folder
                    {
                        Name = "Shared",
                        ProjectId = Id,
                        SubFolders = new List<Folder>(),
                        Documents = new List<Document>(),
                        CreatedBy = "System"
                    };
                    var published = new Folder
                    {
                        Name = "Published",
                        ProjectId = Id,
                        SubFolders = new List<Folder>(),
                        Documents = new List<Document>(),
                        CreatedBy = "System"
                    };
                    var archive = new Folder
                    {
                        Name = "Archive",
                        ProjectId = Id,
                        SubFolders = new List<Folder>(),
                        Documents = new List<Document>(),
                        CreatedBy = "System"
                    };
                    var initialFolders = new List<Folder> { workInProgress, shared, published, archive };

                    // Insert default folders
                    foreach (var folder in initialFolders)
                    {
                        _folderRepository.Insert(folder);
                    }
                    _folderRepository.Save(); // Use async SaveAsync for consistency

                    return View(initialFolders);
                }
                return View(folders);
            }
            catch (Exception ex)
            {
                // Log the exception (use a logging framework like Serilog)
                TempData["Error"] = "An error occurred while loading folders.";
                return RedirectToAction("Index", new { Id });
            }
        }

        // Helper method to recursively load subfolders and documents
        private async Task LoadSubFoldersAsync(Folder folder, IFolderRepository folderRepository)
        {
            // Load subfolders and documents for the current folder
            var subFolders = await folderRepository.GetAllQueryable()
                .Where(f => f.ParentFolderId == folder.Id)
                .Include(f => f.Documents)
                .ToListAsync();

            folder.SubFolders = subFolders;

            // Recursively load subfolders for each child folder
            foreach (var subFolder in subFolders)
            {
                await LoadSubFoldersAsync(subFolder, folderRepository);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Folder(int? id, int projectId)
        {
            try
            {
                Folder? folder;


                folder = await _folderRepository.GetAllQueryable()
                    .Include(f => f.SubFolders).ThenInclude(sf => sf.SubFolders)
                    .Include(f => f.Documents)
                    .ThenInclude(d => d.Versions)
                    .FirstOrDefaultAsync(f => f.Id == id && f.ProjectId == projectId);

                if (folder == null)
                {
                    return NotFound("Folder not found or you don't have access.");
                }

                ViewBag.ProjectId = projectId;
                return View(folder);
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["Error"] = "An error occurred while loading the folder.";
                return RedirectToAction("Index", new { projectId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DeleteFolder(int? folderId)
        {
            if (folderId == null)
                return NotFound();

            var folder = await _folderRepository.GetAllQueryable()
                .FirstOrDefaultAsync(f => f.Id == folderId);

            if (folder == null)
                return NotFound();

            ViewBag.ProjectId = id;

            return View(folder);
        }

        [HttpPost, ActionName("DeleteFolder")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFolderConfirmed(int folderId)
        {
            var folder = _folderRepository.GetAllQueryable().Include(f => f.SubFolders).Include(f => f.Documents).Where(f => f.Id == folderId).FirstOrDefault();
            if (folder == null)
                return NotFound();

            if (folder.SubFolders.Any())
            {
                TempData["Error"] = "Cannot delete a folder that has subfolders.";
                return RedirectToAction(nameof(Index), id);
            }

            _folderRepository.Delete(folder);
            _folderRepository.Save();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult CreateRoot(int projectId)
        {
            ViewBag.ProjectId = projectId;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRoot(int projectId, string folderName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    ModelState.AddModelError("FolderName", "Folder name is required.");
                    ViewBag.ProjectId = projectId;
                    return View();
                }

                var project = _projetcRepository.GetById(projectId);
                if (project == null)
                {
                    return NotFound("Project not found.");
                }


                var root = new Folder
                {
                    Name = folderName,
                    ParentFolderId = null,
                    ProjectId = projectId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = User.Identity.Name ?? "System"
                };

                _folderRepository.Insert(root);
                _folderRepository.Save();

                TempData["Success"] = "Root folder created successfully.";
                return RedirectToAction("Index", new { projectId });
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["Error"] = "An error occurred while creating the root folder.";
                ViewBag.ProjectId = projectId;
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFolder(int parentFolderId, int projectId, string folderName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    TempData["Error"] = "Folder name is required.";
                    return RedirectToAction("Index", new { projectId });
                }

                var parentFolder = await _folderRepository.GetAllQueryable()
                    .FirstOrDefaultAsync(f => f.Id == parentFolderId && f.ProjectId == projectId);
                if (parentFolder == null)
                {
                    return NotFound("Parent folder not found.");
                }

                var folder = new Folder
                {
                    Name = folderName,
                    ParentFolderId = parentFolderId,
                    ProjectId = projectId,
                    SubFolders = new List<Folder>(),
                    Documents = new List<Document>(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = User.Identity.Name ?? "System"
                };

                if (parentFolder.SubFolders == null)
                {
                    parentFolder.SubFolders = new List<Folder>();
                }
                if (parentFolder.Documents == null)
                {
                    parentFolder.Documents = new List<Document>();
                }
                parentFolder.SubFolders.Add(folder);

                _folderRepository.Update(parentFolder);
                _folderRepository.Insert(folder);
                _folderRepository.Save();

                TempData["Success"] = "Folder created successfully.";
                return RedirectToAction("Index", new { projectId });
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["Error"] = "An error occurred while creating the folder.";
                return RedirectToAction("Index", new { projectId });
            }
        }

        [HttpGet]
        public IActionResult Upload(int folderId, int projectId)
        {
            ViewBag.FolderId = folderId;
            ViewBag.ProjectId = projectId;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(int folderId, int projectId, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    TempData["Error"] = "No file selected.";
                    ViewBag.FolderId = folderId;
                    ViewBag.ProjectId = projectId;
                    return View();
                }

                if (file.Length > 10 * 1024 * 1024)
                {
                    TempData["Error"] = "File size exceeds 10MB limit.";
                    ViewBag.FolderId = folderId;
                    ViewBag.ProjectId = projectId;
                    return View();
                }

                var allowedExtensions = new[] { ".pdf", ".docx", ".jpg", ".jepg", ".png", ".dwg", ".ifc", ".rvt", ".pptx" };
                var extension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                {
                    TempData["Error"] = "Invalid file type. Allowed types: PDF, DOCX, JPG, PNG.";
                    ViewBag.FolderId = folderId;
                    ViewBag.ProjectId = projectId;
                    return View();
                }

                var folder = await _folderRepository.GetAllQueryable()
                    .FirstOrDefaultAsync(f => f.Id == folderId && f.ProjectId == projectId);

                if (folder == null)
                {
                    return NotFound("Folder not found.");
                }

                var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", projectId.ToString(), folderId.ToString());
                Directory.CreateDirectory(uploadFolder);

                var filePath = Path.Combine(uploadFolder, file.FileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var document = await _documentRepository.GetAllQueryable()
                    .Include(d => d.Versions)
                    .FirstOrDefaultAsync(d => d.FolderId == folderId && d.Name == Path.GetFileNameWithoutExtension(file.FileName));

                if (document == null)
                {
                    document = new Document
                    {
                        Name = Path.GetFileNameWithoutExtension(file.FileName),
                        FileType = extension,
                        FolderId = folderId,
                        ProjectId = projectId,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = User.Identity.Name ?? "System",
                        Versions = new List<DocumentVersion>()
                    };
                    _documentRepository.Insert(document);
                }

                var version = new DocumentVersion
                {
                    FilePath = filePath,
                    UploadedAt = DateTime.UtcNow,
                    UploadedBy = User.Identity.Name ?? "System",
                    VersionNumber = document.Versions.Count + 1
                };

                document.Versions.Add(version);
                _documentRepository.Save();

                TempData["Success"] = "File uploaded successfully.";
                return RedirectToAction("Index", new { projectId });
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["Error"] = "An error occurred while uploading the file.";
                ViewBag.FolderId = folderId;
                ViewBag.ProjectId = projectId;
                return View();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Download(int versionId, int projectId)
        {
            try
            {
                if (projectId == 0)
                {
                    projectId = id;
                }
                var version = await _documentVersionRepository.GetAllQueryable()
                    .Include(v => v.Document)
                    .FirstOrDefaultAsync(v => v.Id == versionId && v.Document.ProjectId == projectId);

                if (version == null || !System.IO.File.Exists(version.FilePath))
                {
                    return NotFound("File not found.");
                }

                var fullFileName = Path.GetFileName(version.FilePath);
                var fileName = fullFileName.Substring(fullFileName.IndexOf('_') + 1);

                var fileBytes = await System.IO.File.ReadAllBytesAsync(version.FilePath);
                return File(fileBytes, "application/octet-stream", fileName);
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["Error"] = "An error occurred while downloading the file.";
                return RedirectToAction("Index", new { projectId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Versions(int documentId, int projectId)
        {

            try
            {
                if (projectId == 0)
                {
                    projectId = id;
                }
                var document = await _documentRepository.GetAllQueryable()
                    .Include(d => d.Versions)
                    .FirstOrDefaultAsync(d => d.Id == documentId && d.ProjectId == projectId);

                if (document == null)
                {
                    return NotFound("Document not found.");
                }

                ViewBag.ProjectId = projectId;
                return View(document);
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["Error"] = "An error occurred while loading document versions.";
                return RedirectToAction("Index", new { projectId });
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetFolderDetails(int id)
        {
            var folder = await _folderRepository.GetAllQueryable()
                    .Include(f => f.SubFolders).ThenInclude(sf => sf.SubFolders)
                    .Include(f => f.Documents)
                    .ThenInclude(d => d.Versions)
                    .FirstOrDefaultAsync(f => f.Id == id && f.ProjectId == ProjectDocumentController.id);

            if (folder == null)
            {
                return NotFound();
            }

            return PartialView("_FolderDetails", folder);
        }
        [HttpGet]
        public async Task<IActionResult> GetDocumentDetails(int id)
        {
            var folder = await _documentRepository.GetAllQueryable()
                    .Include(d => d.Versions)
                    .Include(d => d.Folder)
                    .FirstOrDefaultAsync(f => f.Id == id && f.ProjectId == ProjectDocumentController.id);

            if (folder == null)
            {
                return NotFound();
            }

            return PartialView("_DocumentDetails", folder);
        }


        [HttpPost]
        public async Task<IActionResult> MoveOrCopyDocument([FromBody] MoveOrCopyVM dto)
        {
            var document = await _context.Documents.FindAsync(dto.DocumentId);
            if (document == null) return NotFound();

            if (dto.ActionType == "copy")
            {

                var newDoc = new Document
                {
                    Name = document.Name,
                    FileType = document.FileType,
                    ProjectId = document.ProjectId,
                    CreatedAt = DateTime.Now,
                    CreatedBy = document.CreatedBy,
                    Versions = new List<DocumentVersion>(),
                    FolderId = dto.TargetFolderId

                };
                _context.Documents.Add(newDoc);
            }
            else if (dto.ActionType == "move")
            {
                document.FolderId = dto.TargetFolderId;
                _context.Documents.Update(document);
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }
        [HttpGet]
        public async Task<IActionResult> OpenFile(int documentId)
        {
            var document = await _documentRepository.GetAllQueryable()
                .Include(d => d.Versions.OrderByDescending(v => v.VersionNumber))
                .FirstOrDefaultAsync(d => d.Id == documentId);

            if (document == null || document.Versions == null || !document.Versions.Any())
            {
                return NotFound(new { message = "Document or version not found." });
            }

            var latestVersion = document.Versions.First();
            var filePath = Path.Combine(_env.WebRootPath, "uploads", document.ProjectId.ToString(), document.FolderId.ToString(), latestVersion.Document.Name + latestVersion.Document.FileType);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { message = "File not found on server." });
            }
            if (document.FileType.ToLower() == ".ifc")
            {
                var relaPath = filePath.Replace(_env.WebRootPath, "").Replace("\\", "/").TrimStart('/');


                return Ok(new
                {
                    fileUrl = $"/{relaPath}", // e.g., /copied-ifc-files/filename.ifc
                    fileType = document.FileType.ToLower()
                });
            }

            if (document.FileType.ToLower() == ".dwg")
            {
                try
                {
                    // Ensure directories exist
                    Directory.CreateDirectory(_uploadsPath);
                    Directory.CreateDirectory(_convertedPath);

                    // Clear old files to avoid conflicts (optional, consider optimizing)
                    foreach (var oldFile in Directory.GetFiles(_uploadsPath))
                    {
                        System.IO.File.Delete(oldFile);
                    }
                    foreach (var oldFile in Directory.GetFiles(_convertedPath))
                    {
                        System.IO.File.Delete(oldFile);
                    }

                    // Generate unique filenames
                    var baseName = Path.GetFileNameWithoutExtension(filePath);
                    var uniqueName = $"{baseName}_{Guid.NewGuid():N}";
                    var dwgFile = Path.Combine(_uploadsPath, uniqueName + ".dwg");

                    // Copy DWG to uploads folder
                    using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    using (var destStream = new FileStream(dwgFile, FileMode.Create, FileAccess.Write))
                    {
                        await sourceStream.CopyToAsync(destStream);
                    }

                    // External tools paths
                    var odaPath = @"C:\Tools\ODAFileConverter\ODAFileConverter.exe";
                    var inkscapePath = @"C:\Program Files\Inkscape\bin\inkscape.exe";

                    if (!System.IO.File.Exists(odaPath))
                    {
                        return StatusCode(500, new { message = $"ODA converter not found at: {odaPath}" });
                    }
                    if (!System.IO.File.Exists(inkscapePath))
                    {
                        return StatusCode(500, new { message = $"Inkscape not found at: {inkscapePath}" });
                    }

                    // Step 1: Convert DWG to DXF
                    var dxfFile = Path.Combine(_convertedPath, uniqueName + ".dxf");
                    var odaArgs = $"\"{Path.GetFullPath(_uploadsPath)}\" \"{Path.GetFullPath(_convertedPath)}\" ACAD2018 DXF 0 1";

                    var odaProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = odaPath,
                        Arguments = odaArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    odaProcess?.WaitForExit();

                    if (!System.IO.File.Exists(dxfFile))
                    {
                        return StatusCode(500, new { message = "DXF file not generated. ODA conversion failed." });
                    }

                    // Step 2: Convert DXF to PDF
                    var pdfFile = Path.Combine(_convertedPath, uniqueName + ".pdf");
                    var inkscapeArgs = $"\"{dxfFile}\" --export-filename=\"{pdfFile}\" --export-area-drawing --export-type=pdf";

                    var inkProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = inkscapePath,
                        Arguments = inkscapeArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    inkProcess?.WaitForExit();

                    if (!System.IO.File.Exists(pdfFile))
                    {
                        return StatusCode(500, new { message = "PDF file not created. Inkscape conversion failed." });
                    }

                    // Construct relative URL for the PDF
                    var relativePath = pdfFile.Replace(_env.WebRootPath, "").Replace("\\", "/").TrimStart('/');
                    return Ok(new
                    {
                        fileUrl = $"/{relativePath}", // e.g., /converted/uniqueName.pdf
                        fileType = ".pdf" // Return as PDF since DWG is converted
                    });
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText("log.txt", $"[{DateTime.Now}] Error: {ex}\n");
                    return StatusCode(500, new { message = $"Unexpected error: {ex.Message}" });
                }
            }

            // Handle PDFs and images
            var relPath = filePath.Replace(_env.WebRootPath, "").Replace("\\", "/").TrimStart('/');
            return Ok(new
            {
                fileUrl = $"/{relPath}", // e.g., /uploads/1/2/document.pdf
                fileType = document.FileType.ToLower()
            });
        }
    }
}
