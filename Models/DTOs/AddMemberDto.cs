using System;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class AddMemberDto
    {
        [EmailAddress]
        public string? UserEmail { get; set; }

        public string? UserDisplayName { get; set; }
    }
}
