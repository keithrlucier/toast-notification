namespace ToastRevival.Api.Services;

public static class EmailTemplates
{
    private const string BgOuter  = "#0A0E1A";
    private const string BgCard   = "#0F1525";
    private const string BgBorder = "#1E2D45";
    private const string Teal     = "#00C9A7";
    private const string TextMain = "#EFF3FF";
    private const string TextSub  = "#8B9BBF";
    private const string TextDim  = "#5A6A8A";

    private static string Wrap(string body) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>Toast Notification</title>
        </head>
        <body style="margin:0;padding:0;background:{BgOuter};font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{BgOuter};padding:48px 16px;">
            <tr><td align="center">
              <table width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">

                <!-- Header -->
                <tr>
                  <td style="padding:0 0 32px;">
                    <table cellpadding="0" cellspacing="0" border="0">
                      <tr>
                        <td style="padding-right:10px;">
                          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                            <path d="M12 3C8.5 3 6 6 6 9.5V14L4 17H20L18 14V9.5C18 6 15.5 3 12 3Z" stroke="{Teal}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                            <path d="M10 17C10 18.1 10.9 19 12 19C13.1 19 14 18.1 14 17" stroke="{Teal}" stroke-width="2" stroke-linecap="round"/>
                          </svg>
                        </td>
                        <td style="font-size:16px;font-weight:600;color:{TextMain};letter-spacing:-0.01em;">Toast Notification</td>
                      </tr>
                    </table>
                  </td>
                </tr>

                <!-- Card -->
                <tr>
                  <td style="background:{BgCard};border:1px solid {BgBorder};border-radius:8px;padding:40px;">
                    {body}
                  </td>
                </tr>

                <!-- Footer -->
                <tr>
                  <td style="padding:24px 0 0;text-align:center;font-size:12px;color:{TextDim};line-height:1.6;">
                    Toast2IT, LLC &nbsp;&middot;&nbsp;
                    <a href="mailto:support@toastnotification.com" style="color:{TextDim};text-decoration:none;">support@toastnotification.com</a>
                    <br />
                    You received this because an account was created with this email address.
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    public static string SetPassword(string fullName, string setPasswordUrl) => Wrap($"""
        <h1 style="margin:0 0 8px;font-size:24px;font-weight:700;color:{TextMain};letter-spacing:-0.01em;">
          Welcome, {System.Web.HttpUtility.HtmlEncode(fullName)}.
        </h1>
        <p style="margin:0 0 32px;font-size:15px;color:{TextSub};line-height:1.6;">
          Your Toast Notification account is verified. Set your password below to access your dashboard.
        </p>
        <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-bottom:32px;">
          <tr>
            <td align="center">
              <a href="{setPasswordUrl}"
                 style="display:inline-block;background:{Teal};color:#0A0E1A;font-size:15px;font-weight:700;
                        text-decoration:none;padding:14px 32px;border-radius:4px;letter-spacing:-0.01em;">
                Set your password
              </a>
            </td>
          </tr>
        </table>
        <p style="margin:0 0 8px;font-size:13px;color:{TextDim};line-height:1.6;">
          This link expires in 24 hours. If you didn&rsquo;t create an account, ignore this email.
        </p>
        <p style="margin:0;font-size:13px;color:{TextDim};line-height:1.6;word-break:break-all;">
          Or copy this URL: <span style="color:{TextSub};">{System.Web.HttpUtility.HtmlEncode(setPasswordUrl)}</span>
        </p>
        """);

    public static string PasswordReset(string? fullName, string resetUrl) => Wrap($"""
        <h1 style="margin:0 0 8px;font-size:24px;font-weight:700;color:{TextMain};letter-spacing:-0.01em;">
          Reset your password
        </h1>
        <p style="margin:0 0 32px;font-size:15px;color:{TextSub};line-height:1.6;">
          Hi {System.Web.HttpUtility.HtmlEncode(fullName ?? "there")} &mdash; we received a request to reset the password on your
          Toast Notification account. Click below to choose a new password.
        </p>
        <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-bottom:32px;">
          <tr>
            <td align="center">
              <a href="{resetUrl}"
                 style="display:inline-block;background:{Teal};color:#0A0E1A;font-size:15px;font-weight:700;
                        text-decoration:none;padding:14px 32px;border-radius:4px;letter-spacing:-0.01em;">
                Reset password
              </a>
            </td>
          </tr>
        </table>
        <p style="margin:0 0 8px;font-size:13px;color:{TextDim};line-height:1.6;">
          This link expires in 1 hour. If you didn&rsquo;t request a reset, ignore this email &mdash; your
          account is still secure.
        </p>
        <p style="margin:0;font-size:13px;color:{TextDim};line-height:1.6;word-break:break-all;">
          Or copy this URL: <span style="color:{TextSub};">{System.Web.HttpUtility.HtmlEncode(resetUrl)}</span>
        </p>
        """);
}
