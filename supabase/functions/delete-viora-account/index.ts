// Requires Supabase Edge Function deployment with auth: 'user' (JWT verified).
// The service-role credentials are supplied by Supabase *server side*.
// NEVER copy them into the Viora executable, repository, or GitHub variables.
import { withSupabase } from 'npm:@supabase/server@^1'

export default {
  fetch: withSupabase({ auth: 'user' }, async (request, context) => {
    if (request.method !== 'POST') {
      return Response.json({ error: 'Method not allowed' }, { status: 405 })
    }

    let body: { confirm?: string }
    try { body = await request.json() }
    catch { return Response.json({ error: 'Invalid request' }, { status: 400 }) }

    if (body.confirm !== 'DELETE') {
      return Response.json({ error: 'Explicit confirmation required' }, { status: 400 })
    }

    const accountId = context.userClaims?.id
    if (!accountId) return Response.json({ error: 'Authentication required' }, { status: 401 })

    // The trusted JWT, not a caller-supplied UUID, selects the account.
    // ON DELETE CASCADE removes public profile, rank and playtime rows.
    const { error } = await context.supabaseAdmin.auth.admin.deleteUser(accountId)
    if (error) return Response.json({ error: 'Could not delete account' }, { status: 500 })

    return Response.json({ deleted: true })
  }),
}
