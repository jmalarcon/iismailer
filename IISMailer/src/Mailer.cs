using System;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Runtime.InteropServices;
using IISHelpers;

namespace IISMailer
{
    internal class Mailer
    {
        private Helper hlpr;

        //Constructor
        public Mailer(Helper h)
        {
            hlpr = h;
        }

        /// <summary>
        /// Sends an email to the specified recipientes (comma separated string) 
        /// with the specified body using the configuration in web.config.
        /// May raise exceptions if there are missing parameters in the configuration file.
        /// It doesn't check if there's a valid sender email address!! If it's not valid it will raise an exception.
        /// If the recipient email addresses are not valid emails it will ignore them
        /// </summary>
        /// <param name="recipients"></param>
        /// <param name="subj"></param>
        /// <param name="body"></param>
        /// <param name="isHTMLContent"></param>
        public void SendMail(string recipients, string subj, string body, bool isHTMLContent=false)
        {
            if (String.IsNullOrEmpty(body))
                return;

            //Email params
            string fromAddress = hlpr.GetParamValue("fromAddress"),
                fromName = hlpr.GetParamValue("fromName"),
                toAddress = recipients,
                subject = subj,
                serverUser = hlpr.GetParamValue("server.user"),
                serverPwd = hlpr.GetParamValue("server.password"),
                serverHost = hlpr.GetParamValue("server.host");
            int serverPort = TypesHelper.DoConvert<int>(hlpr.GetParamValue("server.port", "587"));
            bool serverSSL = TypesHelper.DoConvert<bool>(hlpr.GetParamValue("server.SSL", "true"));

            //At least we'll need from, to and host to try to send the email
            if (fromAddress == "" || toAddress == "" || serverHost == "")
                throw new ArgumentException("One of the needed configuration parameters is missing", "fromAddress, toAddress or server.host");

            //Configure email contents
            MailMessage msg = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName, System.Text.Encoding.UTF8)
            };
            string[] toAddresses = toAddress.Split(',');
            for (int i = 0; i < toAddresses.Length; i++)
            {
                //Catches invalid email addresses
                try
                {
                    msg.To.Add(toAddresses[i]);
                }
                catch { }
            }
            if (msg.To.Count == 0)  //If there are no valid email addresses in the To field, abort email sending
                return;

            msg.Subject = subject;
            msg.SubjectEncoding = System.Text.Encoding.UTF8;
            msg.Body = body;
            msg.BodyEncoding = System.Text.Encoding.UTF8;
            msg.IsBodyHtml = isHTMLContent;

            //Configure email server
            SmtpClient client = new SmtpClient();
            if (!string.IsNullOrEmpty(serverUser))  //If there's a user name for the server (could have an empty password)
                client.Credentials = new NetworkCredential(serverUser, serverPwd);

            client.Port = serverPort;
            client.Host = serverHost;
            client.EnableSsl = serverSSL;

            //Set the security protocol to Tls1.3 or 1.2, as they're the most secure and widely supported (TLS 1.1 and below are deprecated)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12;


            //Try to send the email
            client.Send(msg);
        }

        /// <summary>
        /// Sends an email with the specified body using the configuration and the recipients in web.config.
        /// May raise exceptions if there are missing parameters in the configuration file.
        /// It doesn't check if there's a valid sender email address!! If it's not valid it will raise an exception.
        /// If the recipient email addresses are not valid emails it will ignore them
        /// </summary>
        /// <param name="body">The body in HTML of the email to send</param>
        public void SendMail(string body)
        {
            SendMail(
                hlpr.GetParamValue("toAddress"),
                hlpr.GetParamValue("subject", "New form submission from IISMailer!"), 
                body);
        }
    }
}