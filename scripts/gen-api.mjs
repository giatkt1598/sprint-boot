// Regenerates packages/api-client from the running host code, without a live server:
// an integration test boots the host in containers and writes openapi.json.
import { execSync } from 'node:child_process';

execSync(
  'dotnet test --project apps/api/test/SprintBoot.IntegrationTests -- --filter-method "*OpenApi_document_matches_the_committed_client"',
  { stdio: 'inherit', env: { ...process.env, UPDATE_OPENAPI: '1' } },
);
execSync('pnpm --filter @sprint-boot/api-client generate', { stdio: 'inherit' });
