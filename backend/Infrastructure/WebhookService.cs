using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Infrastructure;
public sealed class WebhookService(TrainingDbContext db,IDataProtectionProvider protection,IConfiguration configuration,IWebHostEnvironment environment)
{
    public static readonly string[] EventTypes=["course.published","course.updated","enrollment.registered","enrollment.approved","enrollment.cancelled","enrollment.completed"];
    private bool AllowLoopback=>environment.IsDevelopment()&&configuration.GetValue("Integrations:AllowLoopbackWebhooks",false);
    public async Task ValidateEndpoint(string address,CancellationToken ct)
    {
        if(!Uri.TryCreate(address,UriKind.Absolute,out var uri)||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Fragment)
            ||uri.Scheme!="https"&&!(AllowLoopback&&uri.IsLoopback&&uri.Scheme=="http"))throw new ApiException(400,"Webhook 必須為 HTTPS，且不可包含帳密或 fragment。");
        var ips=await Dns.GetHostAddressesAsync(uri.DnsSafeHost,ct);if(ips.Length==0||ips.Any(ip=>!Permitted(ip)))throw new ApiException(400,"Webhook 目的地不可為私人、保留或本機網路。");
    }
    private bool Permitted(IPAddress ip)
    {
        if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();if(IPAddress.IsLoopback(ip))return AllowLoopback;
        if(ip.AddressFamily==AddressFamily.InterNetwork){var b=ip.GetAddressBytes();return !(b[0] is 0 or 10 or 127||b[0]>=224||b[0]==169&&b[1]==254||b[0]==172&&b[1]>=16&&b[1]<=31||b[0]==192&&b[1]==168||b[0]==100&&b[1]>=64&&b[1]<=127||b[0]==198&&b[1] is 18 or 19||b[0]==192&&b[1]==0);}
        var v=ip.GetAddressBytes();return ip.AddressFamily==AddressFamily.InterNetworkV6&&!ip.Equals(IPAddress.IPv6Any)&&!ip.IsIPv6LinkLocal&&!ip.IsIPv6Multicast&&!ip.IsIPv6SiteLocal&&(v[0]&0xfe)!=0xfc;
    }
    public string Protect(string secret)=>protection.CreateProtector("training.webhooks.v1").Protect(secret);
    public async Task Expand(CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await AdvancedAccess.Lock(db,"webhook-expand",ct);
        var events=await db.IntegrationEvents.Where(x=>x.ExpandedAt==null).OrderBy(x=>x.OccurredAt).Take(200).ToListAsync(ct);
        var subscriptions=await db.WebhookSubscriptions.Where(x=>x.IsActive).ToListAsync(ct);
        foreach(var e in events){foreach(var s in subscriptions.Where(x=>x.CreatedAt<=e.OccurredAt&&x.EventTypes.Split(' ').Contains(e.Type))){if(!await db.WebhookDeliveries.AnyAsync(x=>x.EventId==e.Id&&x.SubscriptionId==s.Id,ct))db.WebhookDeliveries.Add(new WebhookDelivery{EventId=e.Id,SubscriptionId=s.Id});}e.ExpandedAt=DateTime.UtcNow;}
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task Deliver(CancellationToken ct)
    {
        await Expand(ct);
        for(var i=0;i<20&&!ct.IsCancellationRequested;i++){
            WebhookDelivery? delivery;var lease=Guid.NewGuid();
            await using(var tx=await db.Database.BeginTransactionAsync(ct)){
                delivery=await db.WebhookDeliveries.FromSqlRaw("SELECT TOP(1) * FROM WebhookDeliveries WITH (UPDLOCK,READPAST,READCOMMITTEDLOCK) WHERE Status IN ('Pending','Retry') AND NextAttemptAt<=SYSUTCDATETIME() AND (LeaseUntil IS NULL OR LeaseUntil<SYSUTCDATETIME()) ORDER BY Id").FirstOrDefaultAsync(ct);
                if(delivery is null){await tx.CommitAsync(ct);break;}
                delivery.LeaseToken=lease;delivery.LeaseUntil=DateTime.UtcNow.AddMinutes(2);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            }
            var subscription=await db.WebhookSubscriptions.AsNoTracking().SingleAsync(x=>x.Id==delivery.SubscriptionId,ct);var e=await db.IntegrationEvents.AsNoTracking().SingleAsync(x=>x.Id==delivery.EventId,ct);
            int? httpStatus=null;var outcome="Disabled";
            if(subscription.IsActive){
                try{
                    await ValidateEndpoint(subscription.Endpoint,ct);var uri=new Uri(subscription.Endpoint);
                    using var handler=new SocketsHttpHandler{AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(5),UseProxy=false,
                        ConnectCallback=async(context,token)=>{var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,token);if(addresses.Length==0||addresses.Any(ip=>!Permitted(ip)))throw new HttpRequestException("Destination rejected.");Exception? failure=null;foreach(var ip in addresses){var socket=new Socket(ip.AddressFamily,SocketType.Stream,ProtocolType.Tcp);try{await socket.ConnectAsync(new IPEndPoint(ip,context.DnsEndPoint.Port),token);return new NetworkStream(socket,true);}catch(Exception ex)when(ex is SocketException or OperationCanceledException){socket.Dispose();failure=ex;if(token.IsCancellationRequested)throw;}}throw new HttpRequestException("Connection failed.",failure);}};
                    using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)};
                    var body=System.Text.Json.JsonSerializer.Serialize(new{id=e.Id,type=e.Type,occurredAt=DateTime.SpecifyKind(e.OccurredAt,DateTimeKind.Utc),data=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.PayloadJson)});
                    var timestamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var secret=protection.CreateProtector("training.webhooks.v1").Unprotect(subscription.ProtectedSecret);
                    var signature=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),Encoding.UTF8.GetBytes(timestamp+"."+body))).ToLowerInvariant();
                    using var request=new HttpRequestMessage(HttpMethod.Post,uri){Content=new StringContent(body,Encoding.UTF8,"application/json")};
                    request.Headers.Add("X-Training-Event-Id",e.Id.ToString());request.Headers.Add("X-Training-Timestamp",timestamp);request.Headers.Add("X-Training-Signature","v1="+signature);
                    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);httpStatus=(int)response.StatusCode;outcome=response.IsSuccessStatusCode?"Delivered":"HttpFailure";
                }catch(Exception ex)when(ex is HttpRequestException or SocketException or ApiException or CryptographicException or TaskCanceledException){if(ct.IsCancellationRequested)throw;outcome=ex is ApiException?"DestinationRejected":"TransportFailure";}
            }
            await using(var tx=await db.Database.BeginTransactionAsync(ct)){
                await db.Entry(delivery).ReloadAsync(ct);if(delivery.LeaseToken!=lease){await tx.CommitAsync(ct);continue;}
                delivery.Attempts++;delivery.LastHttpStatus=httpStatus;delivery.LastError=outcome;delivery.LeaseToken=null;delivery.LeaseUntil=null;
                delivery.Status=outcome=="Delivered"?"Delivered":outcome=="Disabled"?"Disabled":delivery.Attempts>=5?"Dead":"Retry";
                delivery.NextAttemptAt=DateTime.UtcNow.AddMinutes(Math.Min(60,Math.Pow(2,delivery.Attempts)));
                db.WebhookAttempts.Add(new WebhookAttempt{DeliveryId=delivery.Id,HttpStatus=httpStatus,Outcome=outcome});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            }
        }
    }
}
