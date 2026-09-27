using System.ComponentModel.DataAnnotations;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Ameli.Security.Tests;
public sealed class AuditContractTests
{
    private static AuditIntegrity Signer(string key="a-long-test-key-that-is-not-used-in-the-application")=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Audit:IntegrityKey"]=key}).Build());
    [Theory][InlineData("ActorName")][InlineData("Action")][InlineData("AfterJson")][InlineData("PreviousHash")][InlineData("SubjectUserId")][InlineData("SessionId")][InlineData("LockoutStartedAtUtc")][InlineData("Id")]
    public void SignedAuditDetectsChangedMetadata(string property)
    {
        var signer=Signer();var e=new SecurityEvent{Id=15,Action="role_change",ActorName="Admin",OccurredAtUtc=DateTimeOffset.UtcNow,IntegrityVersion=1,AfterJson="{}"};
        var hash=signer.EventHash(e);Assert.True(AuditIntegrity.Matches(hash,signer.EventHash(e)));
        object value=property switch{"Id"=>16L,"SessionId" or "SubjectUserId"=>Guid.NewGuid(),"LockoutStartedAtUtc"=>DateTimeOffset.UtcNow,_=>"changed"};
        typeof(SecurityEvent).GetProperty(property)!.SetValue(e,value);Assert.False(AuditIntegrity.Matches(hash,signer.EventHash(e)));
    }
    [Fact]public void KeysAndSignaturesAreValidated(){Assert.Throws<InvalidOperationException>(()=>Signer("short"));Assert.NotEqual(Signer().EventHash(new()),Signer("another-long-test-key-for-unit-tests-only").EventHash(new()));Assert.False(AuditIntegrity.Matches("00","zz"));Assert.False(AuditIntegrity.Matches("00","0000"));}
    [Fact]public void HeadSignatureProtectsTailAndCount(){var s=Signer();var h=new AuditChainHead{LastEventId=15,RecordCount=10,KeyId=s.KeyId,BaselineAtUtc=DateTimeOffset.UtcNow};var hash=s.HeadHash(h);h.LastEventId--;Assert.NotEqual(hash,s.HeadHash(h));h.LastEventId++;h.RecordCount--;Assert.NotEqual(hash,s.HeadHash(h));}
    [Theory][InlineData("01234567",true)][InlineData("123",false)][InlineData("１２３４５６７８",false)][InlineData("1234-678",false)]
    public void PhoneMustHaveEightAsciiDigits(string phone,bool valid){var r=new CreateInternalAccountRequest{Name="Equipo Ameli",Email="equipo@ameli.test",Phone=phone,Role=Roles.Logistics,IsActive=true,Password="Aa123456!",ConfirmPassword="Aa123456!"};Assert.Equal(valid,Validator.TryValidateObject(r,new(r),[],true));}
    [Fact]public void DateRangeAndRequiredStateAreValidated(){var q=new AuditQuery{From=new(2026,9,26),To=new(2026,9,25)};Assert.False(Validator.TryValidateObject(q,new(q),[],true));var r=new InternalAccountRequest{Name="Equipo",Email="equipo@ameli.test",Phone="12345678",Role=Roles.Administrator};Assert.False(Validator.TryValidateObject(r,new(r),[],true));}
}
