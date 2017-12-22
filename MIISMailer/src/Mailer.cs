using System;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;

namespace MIISMailer
{
    public static class Mailer
    {
        /// <summary>
        /// Sends an email with the specified body using the configuration in web.config.
        /// May raise exceptions if there are missing parameters in the configuration file.
        /// It doesn't check if it's a valid email address!! If it's not valid it will raise an exception
        /// </summary>
        /// <param name="body">The body in HTML of the email to send</param>
        public static void SendMail(string body)
        {
            if (String.IsNullOrEmpty(body))
                return;

            //Email params
            string fromAddress = Helper.GetParamValue("mailer.fromAddress"),
                fromName = Helper.GetParamValue("mailer.fromName"),
                toName = Helper.GetParamValue("mailer.toName"),
                toAddress = Helper.GetParamValue("mailer.toAddress"),
                subject = Helper.GetParamValue("mailer.subject", "New form submission from MIISMailer!"),
                serverUser = Helper.GetParamValue("mailer.server.user"),
                serverPwd = Helper.GetParamValue("mailer.server.password"),
                serverHost = Helper.GetParamValue("mailer.server.host");
            int serverPort = Helper.DoConvert<int>(Helper.GetParamValue("mailer.server.port", "587"));
            bool serverSSL = Helper.DoConvert<bool>(Helper.GetParamValue("mailer.server.SSL", "true"));

            //At least we'll need from, to and host to try to send the email
            if (fromAddress == "" || toAddress == "" || serverHost == "")
                throw new ArgumentException("One of the needed configuration parameters is missing", "mailer.fromAddress, mailer.toAddress or mailer.server.host");

            //Configure email contents
            MailMessage msg = new MailMessage();
            msg.From = new MailAddress(fromAddress, fromName, System.Text.Encoding.UTF8);
            string[] toAddresses = toAddress.Split(',');
            for (int i = 0; i < toAddresses.Length; i++)
            {
                msg.To.Add(toAddresses[i]);
            }
            msg.Subject = subject;
            msg.SubjectEncoding = System.Text.Encoding.UTF8;
            msg.Body = body;
            msg.BodyEncoding = System.Text.Encoding.UTF8;
            msg.IsBodyHtml = false;

            //Configure email server
            SmtpClient client = new SmtpClient();
            if (!string.IsNullOrEmpty(serverUser))  //If there's a user name for the server (could have an empty password)
                client.Credentials = new NetworkCredential(serverUser, serverPwd);

            client.Port = serverPort;
            client.Host = serverHost;
            client.EnableSsl = serverSSL;

            //Try to send the email
            client.Send(msg);
        }
    }
}