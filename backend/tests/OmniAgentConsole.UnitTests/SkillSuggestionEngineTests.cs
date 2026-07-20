using System;
using System.Collections.Generic;
using System.Linq;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class SkillSuggestionEngineTests
{
    private static readonly List<SkillDefinition> Skills =
    [
        Make("Node.js + Express + TypeScript API", "Backend", "node,node.js,nodejs,express,typescript"),
        Make("Go REST API", "Backend", "go,golang,gin,fiber,chi"),
        Make("Python FastAPI Service", "Backend", "fastapi,python,uvicorn,pydantic"),
        Make("Java Spring Boot API", "Backend", "java,spring,spring boot"),
        Make("Angular Frontend", "Frontend", "angular,angularjs"),
        Make("React Frontend", "Frontend", "react,reactjs,vite,web sitesi,website,frontend,landing"),
        Make("Flutter App", "Frontend", "flutter,dart,mobil,mobile,android,ios"),
        Make("JWT Authentication", "Security", "jwt,json web token,bcrypt,login,giris,kayit"),
        Make("Input Validation (Zod/Joi)", "Quality", "validation,joi,zod,dogrulama,input validation"),
        Make("Dockerized Service", "Packaging", "docker,dockerize,dockerfile,docker-compose"),
        Make("PostgreSQL + Migrations", "Data", "postgres,postgresql,psql,migration"),
        Make("Redis Caching", "Data", "redis,cache,caching,onbellek"),
        Make("Health Checks & Observability", "Quality", "health check,healthcheck,health"),
        Make("Relational Database + ORM", "Data", "orm,sqlalchemy,prisma,typeorm,ef core"),
        Make("Swagger / OpenAPI", "Quality", "swagger,openapi,swagger ui,api docs,api dokumantasyon"),
    ];

    private static SkillDefinition Make(string name, string category, string keywords) =>
        new() { Name = name, Category = category, Keywords = keywords, Instructions = "x", Enabled = true };

    private static IReadOnlyList<string> SuggestNames(string prompt)
    {
        var result = SkillSuggestionEngine.Suggest(prompt, Skills);
        return Skills.Where(s => result.SkillIds.Contains(s.Id)).Select(s => s.Name).ToList();
    }

    [Fact]
    public void GoRedisPostgresPrompt_SuggestsMatchingStack()
    {
        var names = SuggestNames(
            "PostgreSQL veritabanindan kullanici bilgilerini ceken ve sik sorgulanan verileri Redis uzerinde cache'leyen, " +
            "yuksek performansli bir Go REST API yaz. Redis baglantisi icin retry mekanizmasi ekle. " +
            "Tum yapiyi ayaga kaldiracak docker-compose dosyasini ve health check endpoint'ini de hazirla.");

        Assert.Contains("Go REST API", names);
        Assert.Contains("Redis Caching", names);
        Assert.Contains("PostgreSQL + Migrations", names);
        Assert.Contains("Dockerized Service", names);
        Assert.Contains("Health Checks & Observability", names);
        Assert.DoesNotContain("Python FastAPI Service", names);
    }

    [Fact]
    public void NodeJwtPrompt_SuggestsNodeSecurityValidationDocker()
    {
        var result = SkillSuggestionEngine.Suggest(
            "Node.js, Express ve TypeScript kullanarak JWT tabanli bir kullanici kayit ve giris sistemi yaz. " +
            "Sifreleri bcrypt ile hashle. API endpoints icin input validation (Joi veya Zod ile) ekle. " +
            "APInin dockerize edilmesini sagla.", Skills);
        var names = Skills.Where(s => result.SkillIds.Contains(s.Id)).Select(s => s.Name).ToList();

        Assert.Contains("Node.js + Express + TypeScript API", names);
        Assert.Contains("JWT Authentication", names);
        Assert.Contains("Input Validation (Zod/Joi)", names);
        Assert.Contains("Dockerized Service", names);
        Assert.Empty(result.Questions);
    }

    [Fact]
    public void FastApiPostgresPrompt_SuggestsFastApiPostgresOrm()
    {
        var names = SuggestNames(
            "FastAPI kullanarak PostgreSQL veritabanindaki satis verilerini analiz eden bir servis yaz. " +
            "SQL enjeksiyon aciklarina karsi ORM (SQLAlchemy) kullan. Dockerfile ve migration script'ini hazirla.");

        Assert.Contains("Python FastAPI Service", names);
        Assert.Contains("PostgreSQL + Migrations", names);
        Assert.Contains("Relational Database + ORM", names);
        Assert.Contains("Dockerized Service", names);
    }

    [Fact]
    public void VaguePrompt_AsksStackQuestion()
    {
        var result = SkillSuggestionEngine.Suggest("Bir stok takip uygulamasi hazirlamak istiyorum lutfen", Skills);

        Assert.Empty(result.SkillIds);
        Assert.Contains(result.Questions, q => q.Contains("dil/framework"));
    }

    [Fact]
    public void DatabaseMentionWithoutSpecificDb_AsksDatabaseQuestion()
    {
        var result = SkillSuggestionEngine.Suggest(
            "Node.js ile bir API yaz, veritabanina kayit atsin ama detayini sonra secelim", Skills);

        Assert.Contains(result.Questions, q => q.Contains("veritaban"));
    }

    [Fact]
    public void SwaggerPrompt_SuggestsOpenApiSkill()
    {
        var names = SuggestNames(
            "FastAPI ile not API yaz, Swagger UI ve openapi.json olsun, ornek request body'ler de gelsin.");

        Assert.Contains("Python FastAPI Service", names);
        Assert.Contains("Swagger / OpenAPI", names);
    }

    [Fact]
    public void WebsitePrompt_SuggestsReactFrontend()
    {
        var names = SuggestNames(
            "Quantum islemci satan firmanin modern kurumsal web sitesini yaz, landing page olsun.");

        Assert.Contains("React Frontend", names);
        Assert.DoesNotContain("Flutter App", names);
    }

    [Fact]
    public void AngularKeyword_SuggestsAngularFrontend()
    {
        var names = SuggestNames("Angular ile kurumsal bir dashboard arayuzu olustur.");
        Assert.Contains("Angular Frontend", names);
    }

    [Fact]
    public void MobilePrompt_SuggestsFlutter()
    {
        var names = SuggestNames("Flutter ile mobil android ve ios uygulamasi yaz.");
        Assert.Contains("Flutter App", names);
    }

    [Fact]
    public void GoKeyword_DoesNotMatchInsideOtherWords()
    {
        var result = SkillSuggestionEngine.Suggest(
            "Django kullanarak kategori bazli bir blog uygulamasi yaz, python ile", Skills);
        var names = Skills.Where(s => result.SkillIds.Contains(s.Id)).Select(s => s.Name).ToList();

        Assert.DoesNotContain("Go REST API", names);
        Assert.Contains("Python FastAPI Service", names);
    }

    [Fact]
    public void ShortPrompt_ReturnsNothing()
    {
        var result = SkillSuggestionEngine.Suggest("go api", Skills);
        Assert.Empty(result.SkillIds);
        Assert.Empty(result.Questions);
    }
}
