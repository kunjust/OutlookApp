using System.Collections.Generic;
using System.Threading.Tasks;
using OutlookApp.Models;

/// <summary>
/// 邮件服务接口，定义获取邮件的标准方法。
/// 由 ImapEmailService 和 GraphEmailService 分别实现。
/// </summary>
namespace OutlookApp.Services;

public interface IEmailService
{
    Task<bool> VerifyAsync(EmailAccount account);
    Task<List<EmailMessage>> FetchEmailsAsync(EmailAccount account, int maxCount = 50);
}
