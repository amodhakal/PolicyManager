async function addPolicyHolder() {
  const data = {
    email: `${crypto.randomUUID()}@gmail.com`,
    firstName: crypto.randomUUID(),
    lastName: crypto.randomUUID(),
  };

  const res = await fetch(
    "https://policymanager-api-fdhafuhzfagyajby.centralus-01.azurewebsites.net/api/policyholders",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(data),
    },
  );

  console.log(res.status);
  console.log(await res.json());
}

const policyHolderRequests = [] as Promise<void>[];
for (let i = 0; i < 7_417; i++) {
  policyHolderRequests.push(addPolicyHolder());
}

await Promise.all(policyHolderRequests);
