"use strict";
//set of helper methods for get, post, put
//also makes it easier to ensure the auth token is part of every request when we come through here
Object.defineProperty(exports, "__esModule", { value: true });
exports.put = exports.postWithNoResponse = exports.post = exports.remove = exports.get = void 0;
async function get({ path, query }) {
    const response = await executeFetch(path, query, "GET");
    return response.json();
}
exports.get = get;
async function remove({ path, query }) {
    return executeFetch(path, query, "DELETE");
}
exports.remove = remove;
async function post({ path, body }) {
    const response = await executeFetch(path, body, "POST");
    return response.json();
}
exports.post = post;
async function postWithNoResponse({ path, body }) {
    await executeFetch(path, body, "POST");
}
exports.postWithNoResponse = postWithNoResponse;
async function put({ path, body }) {
    return executeFetch(path, body, "PUT");
}
exports.put = put;
async function executeFetch(path, bodyOrQuery, method) {
    const options = {
        method,
        headers: {}
    };
    let url = "";
    if (method === "GET" || method === "DELETE") {
        const searchString = createSearchParams(bodyOrQuery);
        url = buildUrl(path, searchString);
    }
    else {
        //I.e. POST, PUT
        url = buildUrl(path);
        //set the necessary headers so we can post a body
        //don't do this for form data
        options.headers = {
            'Accept': 'application/json',
            'Content-Type': 'application/json'
        };
        //put that body in the request
        options.body = JSON.stringify(bodyOrQuery);
        options.cache = "no-store";
    }
    const response = await fetch(url, options);
    if (!response.ok) {
        console.error(`FAILED TO FETCH`, "|", url, "|", response);
        const error = await response.json();
        throw new Error(JSON.stringify(error));
    }
    return response;
}
function buildUrl(path, searchString) {
    //append the path to the base URL, taken from environment
    const apiBaseUrl = window.electron.apiUrl;
    let url = `${apiBaseUrl}/${path}`;
    //if we have a search string, append it as a query
    if (searchString) {
        url += `?${searchString}`;
    }
    return url;
}
function createSearchParams(params) {
    if (!params)
        return null;
    const searchParams = new URLSearchParams();
    const appendParams = (key, value) => {
        //skip null or undefined values
        if (value === null || value === undefined) {
            return;
        }
        if (typeof value === "object" && !Array.isArray(value)) {
            //handle nested objects
            Object.entries(value).forEach(([nestedKey, nestedValue]) => {
                appendParams(`${key}.${nestedKey}`, nestedValue);
            });
        }
        else if (Array.isArray(value)) {
            //check if the array contains objects
            if (value.every((item) => (typeof item === "string" || typeof item === "number" || typeof item === "boolean") && item !== null)) {
                //simple types
                value.forEach((item) => {
                    searchParams.append(key, String(item));
                });
            }
            else {
                //array of objects
                value.forEach((arrayValue, index) => {
                    appendParams(`${key}[${index}]`, arrayValue);
                });
            }
        }
        else {
            //handle simple key-value pairs
            searchParams.append(key, String(value));
        }
    };
    Object.entries(params).forEach(([key, value]) => {
        appendParams(key, value);
    });
    return searchParams.toString();
}
//# sourceMappingURL=api-helper.js.map