using System;
using System.Web;
using System.IO;
using System.Security;
using IISHelpers;

namespace IISMailer
{
    public class IISMailerHandler : IHttpHandler
    {
        #region IHttpHandler Members

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext ctx)
        {
            try
            {
                HttpRequest req = ctx.Request;

                //Try to process the definition file to send an email
                string filePath = ctx.Server.MapPath(ctx.Request.FilePath);
                Helper hlpr = new Helper(filePath);    //Takes care of reading property values and some other helper tasks

                //First we check if the form is sent from the correct domain (by default, the current domain)
                bool isAllowed = false;
                if (req.UrlReferrer == null)
                {
                    throw new SecurityException();  //Forbid direct call
                }
                string referrerDomain = req.UrlReferrer.Host;
                string[] allowedDomains = hlpr.GetParamValue("allowedDomains", req.Url.Host).Split(',');
                //Must have a referrer to work
                for (int i = 0; i < allowedDomains.Length; i++)
                {
                    if (referrerDomain == allowedDomains[i])
                    {
                        isAllowed = true;
                        break;
                    }
                }

                //and check the spam prevention field (miis-email-hpt) (from IIS Emailer Honeypot) with any value
                if (!string.IsNullOrEmpty(req.Form[Helper.HONEYPOT_FIELD_NAME]))
                    isAllowed = false;

                if (!isAllowed)
                {
                    throw new SecurityException();
                }

                //Process form data (excluding special params suchs a honeypot or the final URL)
                string formData = hlpr.GetFormDataForEmailFromRequest();
                //Email form data
                Mailer mlr = new Mailer(hlpr);
                mlr.SendMail(formData);

                //Call webhook if any
                hlpr.CallWebHook();

                //Save to CSV File
                hlpr.AppendToCSVFile();


                //Send response to the user that filled in the form
                hlpr.SendResponseToFormSender();

                //Redirect to final URL
                ctx.Response.Redirect(hlpr.GetDestinationURL());

            }
            catch (FileNotFoundException)
            {
                //File does not exist
                ctx.Response.StatusDescription = "File not found";
                ctx.Response.StatusCode = 404;
            }
            catch (SecurityException)
            {
                //Access to file not allowed
                ctx.Response.StatusDescription = "Forbidden";
                ctx.Response.StatusCode = 403;
            }
            catch (Exception)
            {
                throw;
            }
        }

#endregion
    }
}
