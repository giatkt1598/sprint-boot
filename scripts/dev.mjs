// Starts the whole development environment with one command.
import { execSync } from 'node:child_process';

const run = (command, env = {}) =>
  execSync(command, { stdio: 'inherit', env: { ...process.env, ...env } });

run('docker compose -f deploy/docker/compose.yml up -d --wait');
run('dotnet run --project apps/api/src/SprintBoot.DbMigrator', { DOTNET_ENVIRONMENT: 'Development' });
run(
  'pnpm exec concurrently --names api,web --prefix-colors blue,green --kill-others' +
    ' "dotnet run --project apps/api/src/SprintBoot.Host"' +
    ' "pnpm --filter @sprint-boot/web dev"',
);
