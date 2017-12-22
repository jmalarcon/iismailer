using System;
using System.Web;

namespace MIISMailer
{
    public class MIISMailerHandler : IHttpHandler
    {
        #region IHttpHandler Members

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext ctx)
        {
            HttpRequest req = ctx.Request;

            //First we check if the form is sent from the correct domain (by default, the current domain)
            bool isAllowed = false;
            if (req.UrlReferrer == null)
            {
                return;
            }
            string referrerDomain = req.UrlReferrer.Host;
            string[] allowedDomains = Helper.GetParamValue("mailer.alllowedDomains", req.Url.Host).Split(',');
            //Must have a referrer to work
            for(int i=0; i<allowedDomains.Length; i++)
            {
                if(referrerDomain == allowedDomains[i])
                {
                    isAllowed = true;
                    break;
                }
            }

            //and check the spam prevention field (miis-email-hpt) (from MIIS Emailer Honeypot) with any value
            if (!string.IsNullOrEmpty(req.Form[Helper.HONEYPOT_FIELD_NAME]))
                isAllowed = false;

            //TODO: Possibly add a maximum emails sent per minute check using caching

            if (!isAllowed)
            {
                ctx.Response.StatusDescription = "Forbidden";
                ctx.Response.StatusCode = 403;
                return;
            }

            //Process form data (excluding special params suchs a honeypot or the final URL)
            string formData = Helper.GetFormDataForEmailFromRequest();

            //Save to CSV File
            Helper.AppendToCSVFile();

            //Email form data
            Mailer.SendMail(formData);

            //Redirect to final URL
            ctx.Response.Redirect(Helper.GetDestinationURL());
        }

#endregion
    }
}
