# --- Этап 1: Сборка ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем csproj и восстанавливаем зависимости (кэшируется)
COPY ["DiscordBirge/DiscordBirge.csproj", "DiscordBirge/"]
RUN dotnet restore "DiscordBirge/DiscordBirge.csproj"

# Копируем всё остальное и собираем
COPY . .
WORKDIR "/src/DiscordBirge"
RUN dotnet publish "DiscordBirge.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- Этап 2: Запуск ---
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

# Копируем только собранные файлы
COPY --from=build /app/publish .

# Точка входа (имя dll зависит от твоего проекта)
ENTRYPOINT ["dotnet", "DiscordBirge.dll"]