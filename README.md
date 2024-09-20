# Podcasts hosting app
TODO: complete decription

### Local development
Trust ASP.NET dev cert:
```
dotnet dev-certs https --trust
```
Linux has a bit more complicated setup, but console output should lead you to the detailed guide, but it didn't work for me.
Instead I used these commands:
```
dotnet dev-certs https
sudo -E dotnet dev-certs https -ep /usr/local/share/ca-certificates/aspnet/https.crt --format PEM
sudo update-ca-certificates
```

Install useful dotnet tools:
- `dotnet tool install -g dotnet-ef`
- `dotnet tool install -g dotnet-aspnet-codegenerator`

Use `dotnet user-secrets`
```
dotnet user-secrets set "ConnectionStrings:PodcastsHosting" "<some valid sql connection string>"
dotnet user-secrets set "Storage:ConnectionString" "UseDevelopmentStorage=true;" # for local azurite
dotnet ef database update
```

