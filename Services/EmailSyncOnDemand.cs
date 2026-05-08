using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OutlookApp.Api;
using OutlookApp.Models;
using OutlookApp.Services;

namespace OutlookApp.Services;

public class EmailSyncOnDemand
{
    private readonly DatabaseService _dbService;
    private readonly ImapEmailService _imapService;
    private readonly VerificationExtractor _extractor;

    public EmailSyncOnDemand(DatabaseService dbService, ImapEmailService imapService)
    {
        _dbService = dbService;
        _imapService = imapService;
        _extractor = new VerificationExtractor(dbService.ConnectionString);
    }

    public async Task<(bool Success, string Code, DateTime ReceivedTime)> FetchVerificationCodeAsync(EmailAccount account)
    {
        var (code, receivedTime) = _extractor.ExtractLatestCode(account.Email);
        if (!string.IsNullOrEmpty(code))
        {
            _dbService.UpdateAccountCodeAndSyncTime(account.Email, code, DateTime.Now);
            return (true, code, receivedTime);
        }

        try
        {
            var messages = await _imapService.FetchByPasswordAsync(account, 20);
            if (messages.Count > 0)
            {
                _dbService.SaveMessages(account.Id, messages);
            }
        }
        catch
        {
            if (!string.IsNullOrEmpty(account.Token))
            {
                try
                {
                    var messages = await _imapService.FetchByXoauth2Async(account.Email, account.Token, 20);
                    if (messages.Count > 0)
                    {
                        _dbService.SaveMessages(account.Id, messages);
                    }
                }
                catch { }
            }
        }

        (code, receivedTime) = _extractor.ExtractLatestCode(account.Email);
        if (!string.IsNullOrEmpty(code))
        {
            _dbService.UpdateAccountCodeAndSyncTime(account.Email, code, DateTime.Now);
            return (true, code, receivedTime);
        }

        return (false, string.Empty, DateTime.MinValue);
    }
}
