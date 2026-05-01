using System.Collections.Generic;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

public interface IEmailService
{
    Task<bool> VerifyAsync(EmailAccount account);
    Task<List<EmailMessage>> FetchEmailsAsync(EmailAccount account, int maxCount = 50);
}
