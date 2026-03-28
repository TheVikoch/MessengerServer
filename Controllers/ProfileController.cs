using System;
using System.Threading.Tasks;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly IUserProfileService _profileService;

        public ProfileController(IUserProfileService profileService)
        {
            _profileService = profileService;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile()
        {
            return await Execute(async userId => Ok(await _profileService.GetProfileAsync(userId, userId)));
        }

        [HttpGet("{userId:guid}")]
        public async Task<IActionResult> GetProfile(Guid userId)
        {
            return await Execute(async requesterId => Ok(await _profileService.GetProfileAsync(requesterId, userId)));
        }

        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateUserProfileDto request)
        {
            return await Execute(async userId => Ok(await _profileService.UpdateMyProfileAsync(userId, request)));
        }

        [HttpPost("me/photos/init")]
        public async Task<IActionResult> InitPhotoUpload([FromBody] InitUserProfilePhotoUploadRequestDto request)
        {
            return await Execute(async userId => Ok(await _profileService.InitPhotoUploadAsync(userId, request)));
        }

        [HttpPost("me/photos/complete")]
        public async Task<IActionResult> CompletePhotoUpload([FromBody] CompleteUserProfilePhotoUploadRequestDto request)
        {
            return await Execute(async userId => Ok(await _profileService.CompletePhotoUploadAsync(userId, request)));
        }

        [HttpDelete("me/photos/{photoId:guid}")]
        public async Task<IActionResult> DeletePhoto(Guid photoId)
        {
            return await Execute(async userId => Ok(await _profileService.DeletePhotoAsync(userId, photoId)));
        }

        [HttpGet("{userId:guid}/photos/{photoId:guid}/url")]
        public async Task<IActionResult> GetPhotoDownloadUrl(Guid userId, Guid photoId)
        {
            return await Execute(async requesterId => Ok(await _profileService.GetPhotoDownloadUrlAsync(requesterId, userId, photoId)));
        }

        private async Task<IActionResult> Execute(Func<Guid, Task<IActionResult>> action)
        {
            try
            {
                var userId = GetCurrentUserId();
                return await action(userId);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private Guid GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")
                ?? User.FindFirst("userId");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid user token");
            }

            return userId;
        }
    }
}
