using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class MediaApiTests(AuthApiFactory factory):IClassFixture<AuthApiFactory>
{
    [Theory][InlineData("GET")][InlineData("POST")][InlineData("PUT")][InlineData("DELETE")]
    public async Task Admin_media_routes_require_authentication(string method)
    { using var request=new HttpRequestMessage(new HttpMethod(method),method=="GET"||method=="POST"?"/api/v1/admin/media":"/api/v1/admin/media/99999999-9999-9999-9999-999999999999");if(method=="POST")request.Content=new MultipartFormDataContent();if(method=="PUT")request.Content=JsonContent.Create(new{altText="Alt",mediaType="IMAGE"});var response=await factory.CreateClient().SendAsync(request);Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); }

    [Fact] public async Task Authenticated_admin_can_list_upload_update_and_delete_media()
    { var client=AuthenticatedClient();var list=await client.GetAsync("/api/v1/admin/media?page=1&pageSize=20&mediaType=IMAGE&search=asset");Assert.Equal(HttpStatusCode.OK,list.StatusCode);using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent([0xff,0xd8,0xff]),"file","photo.jpg");form.Add(new StringContent("IMAGE"),"mediaType");form.Add(new StringContent("Portrait"),"altText");form.First().Headers.ContentType=new MediaTypeHeaderValue("image/jpeg");var upload=await client.PostAsync("/api/v1/admin/media",form);Assert.Equal(HttpStatusCode.Created,upload.StatusCode);var id="99999999-9999-9999-9999-999999999999";var update=await client.PutAsJsonAsync($"/api/v1/admin/media/{id}",new{altText="Updated",mediaType="IMAGE"});Assert.Equal(HttpStatusCode.OK,update.StatusCode);var delete=await client.DeleteAsync($"/api/v1/admin/media/{id}");Assert.Equal(HttpStatusCode.NoContent,delete.StatusCode); }

    [Fact] public async Task Media_api_normalizes_validation_and_in_use_conflict_without_secrets()
    { var client=AuthenticatedClient();var invalid=await client.GetAsync("/api/v1/admin/media?page=0&pageSize=101&mediaType=SCRIPT");Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);var body=await invalid.Content.ReadAsStringAsync();Assert.Contains("VALIDATION_ERROR",body);Assert.DoesNotContain("SecretAccessKey",body);var conflict=await client.DeleteAsync("/api/v1/admin/media/88888888-8888-8888-8888-888888888888");Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);Assert.Contains("MEDIA_IN_USE",await conflict.Content.ReadAsStringAsync()); }

    private HttpClient AuthenticatedClient(){var client=factory.CreateClient();using var scope=factory.Services.CreateScope();var token=scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(AuthApiFactory.AdminId,"admin@example.com");client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.Value);return client;}
}
