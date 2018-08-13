using System;
using System.Web;
using System.IO;
using System.Security;

namespace IISMailer
{
    public class IISMailerHandler : IHttpAsyncHandler
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
                Helper hlpr = new Helper(filePath, ctx);    //Takes care of reading property values and some other helper tasks

                //First we check if the form is sent from the correct domain (by default, the current domain)
                bool isAllowed = false;
                if (req.UrlReferrer == null)
                {
                    throw new SecurityException();  //Forbid direct call
                }
                string referrerDomain = req.UrlReferrer.Host;
#if PROFESSIONAL || DEBUG
                string[] allowedDomains = hlpr.GetParamValue("allowedDomains", req.Url.Host).Split(',');
#else
                string[] allowedDomains = (req.Url.Host).Split(',');
#endif
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

#if PROFESSIONAL || DEBUG
                //Call webhook if any
                hlpr.CallWebHook();
#endif
                //Save to CSV File
                hlpr.AppendToCSVFile();

#if PROFESSIONAL || DEBUG
                //Send response to the user that filled in the form
                hlpr.SendResponseToFormSender();
#endif
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

#region IHttpAsyncHandler members
        private delegate void AsyncRequestDelegate(HttpContext context);
        private AsyncRequestDelegate procRequest;

        public IAsyncResult BeginProcessRequest(HttpContext context, AsyncCallback cb, object extraData)
        {
            procRequest = new AsyncRequestDelegate(ProcessRequest);
            return procRequest.BeginInvoke(context,cb, extraData);
        }

        public void EndProcessRequest(IAsyncResult result)
        {
            procRequest.EndInvoke(result);
        }
#endregion
    }
}
